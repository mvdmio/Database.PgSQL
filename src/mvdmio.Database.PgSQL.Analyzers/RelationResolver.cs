using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Resolves every declared relation against the set of parsed table definitions, which is the only stage that sees
///    more than one table at a time.
/// </summary>
/// <remarks>
///    Relations are one-directional and are never paired with the declaration facing the other way: each is
///    self-sufficient, the provider's own association metadata is one-directional, and pairing would buy a matching
///    rule and ambiguity diagnostics for nothing at translation time. A cycle is therefore ordinary — a relation to a
///    parent alongside a relation to that parent's children already is one.
/// </remarks>
internal static class RelationResolver
{
   internal sealed class ResolveResult
   {
      public ResolveResult(ImmutableArray<ResolvedTable> tables, ImmutableArray<Diagnostic> diagnostics)
      {
         Tables = tables;
         Diagnostics = diagnostics;
      }

      /// <summary>Every table definition, each paired with the relations of its own that resolved.</summary>
      public ImmutableArray<ResolvedTable> Tables { get; }

      public ImmutableArray<Diagnostic> Diagnostics { get; }
   }

   public static ResolveResult Resolve(ImmutableArray<TableDefinitionModel> models)
   {
      var byFullName = models.ToImmutableDictionary(x => x.TableClassFullName, StringComparer.Ordinal);
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
      var tables = ImmutableArray.CreateBuilder<ResolvedTable>(models.Length);

      foreach (var model in models)
      {
         var relations = ImmutableArray.CreateBuilder<ResolvedRelation>(model.Relations.Length);

         CheckForForgottenConditions(model, byFullName, diagnostics);

         foreach (var relation in model.Relations)
         {
            if (TryResolve(model, relation, byFullName, diagnostics, out var result))
               relations.Add(result);
         }

         tables.Add(new ResolvedTable(model, relations.ToImmutable()));
      }

      return new ResolveResult(tables.ToImmutable(), diagnostics.ToImmutable());
   }

   private static bool TryResolve(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out ResolvedRelation resolved
   )
   {
      resolved = null!;

      if (!byFullName.TryGetValue(relation.TargetClassFullName, out var target))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationTargetIsNotATableDefinition,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               relation.TargetTypeDisplayName
            )
         );

         return false;
      }

      // A relation declared as a RelationDefinition<,> class already states both sides of each pair itself, so there
      // is no foreign-key/primary-key side to work out from cardinality — that only applies to the old
      // attribute-argument form below.
      if (relation.IsDefinitionForm)
         return TryResolveDefinitionForm(model, relation, target, diagnostics, out resolved);

      // Cardinality decides one thing: which side holds the foreign key and which holds the primary key it joins. A
      // relation to one row is resolved through a foreign key on the declaring type, one to many through a foreign key
      // on the target. The far end is always the other side's primary key, which may have more than one member — and
      // then the declaration names one foreign-key property per member, paired positionally against it.
      var (foreignKeyOwner, primaryKeyOwner) = relation.IsToMany ? (target, model) : (model, target);
      var primaryKeys = primaryKeyOwner.PrimaryKeys;

      if (relation.ForeignKeyPropertyNames.Length != primaryKeys.Length)
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationForeignKeyArityMismatch,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               relation.ForeignKeyPropertyNames.Length,
               DescribeNames(relation.ForeignKeyPropertyNames),
               primaryKeyOwner.TableClassName,
               primaryKeys.Length
            )
         );

         return false;
      }

      // Each phase below reports every problem it finds rather than only the first, because each is a separate mistake.
      // Whether the phase found any is read off the diagnostic count, so no phase carries a flag of its own.
      var foreignKeys = ImmutableArray.CreateBuilder<PropertyDefinitionModel>(primaryKeys.Length);
      var beforeResolving = diagnostics.Count;

      foreach (var foreignKeyPropertyName in relation.ForeignKeyPropertyNames)
      {
         var foreignKey = foreignKeyOwner.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, foreignKeyPropertyName, StringComparison.Ordinal));

         if (foreignKey is null)
         {
            diagnostics.Add(
               Diagnostic.Create(
                  TableRepositoryDiagnostics.RelationForeignKeyNotFound,
                  relation.Location,
                  model.TableClassName,
                  relation.PropertyName,
                  foreignKeyPropertyName,
                  foreignKeyOwner.TableClassName
               )
            );

            continue;
         }

         foreignKeys.Add(foreignKey);
      }

      // Nothing can be type-checked until every name resolves.
      if (diagnostics.Count != beforeResolving)
         return false;

      var beforeTypeChecking = diagnostics.Count;

      for (var position = 0; position < primaryKeys.Length; position++)
      {
         if (CanJoin(foreignKeys[position].TypeName, primaryKeys[position].TypeName))
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationForeignKeyTypeMismatch,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               foreignKeys[position].PropertyName,
               foreignKeys[position].TypeName,
               primaryKeys[position].PropertyName,
               primaryKeys[position].TypeName,
               position + 1
            )
         );
      }

      if (diagnostics.Count != beforeTypeChecking)
         return false;

      // Each pair states which side is the declaring table's own property and which is the target's, exactly like
      // the definition form — the only difference is that cardinality, rather than the pair itself, decides which of
      // foreignKeys/primaryKeys plays which role here.
      var resolvedForeignKeys = foreignKeys.ToImmutable();

      var joinedKeys = primaryKeys
         .Select(
            (primaryKey, position) => relation.IsToMany
               ? new JoinedKeyPair(primaryKey, resolvedForeignKeys[position])
               : new JoinedKeyPair(resolvedForeignKeys[position], primaryKey)
         )
         .ToImmutableArray();

      // The uniqueness and tenancy claims read the resolved pairs themselves, so they apply here exactly as they do
      // to a relation declared through the RelationDefinition<,> form below — a pair is a pair regardless of how it
      // was written. Nullable-unique is the one check here that drops the relation; the rest are warnings.
      if (!CheckKeyPairClaims(model, relation, target, joinedKeys, diagnostics))
         return false;

      resolved = new ResolvedRelation(
         propertyName: relation.PropertyName,
         isToMany: relation.IsToMany,
         targetDataTypeName: QualifyTypeName(target.NamespaceName, target.DataTypeName),
         joinedKeys: joinedKeys
      );

      return true;
   }

   /// <summary>
   ///    Resolves a relation declared as a <c>RelationDefinition&lt;,&gt;</c> class. Each pair already names its own
   ///    declaring-side and target-side property, so there is no foreign-key/primary-key side to work out from
   ///    cardinality — resolving is just looking each name up on its own table's mapped columns.
   /// </summary>
   private static bool TryResolveDefinitionForm(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out ResolvedRelation resolved
   )
   {
      resolved = null!;

      var pairs = relation.KeyPairs!.Value;
      var joinedKeys = ImmutableArray.CreateBuilder<JoinedKeyPair>(pairs.Length);
      var beforeResolving = diagnostics.Count;

      foreach (var pair in pairs)
      {
         var declaringProperty = model.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, pair.DeclaringPropertyName, StringComparison.Ordinal));
         var targetProperty = target.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, pair.TargetPropertyName, StringComparison.Ordinal));

         if (declaringProperty is null || targetProperty is null)
         {
            diagnostics.Add(
               Diagnostic.Create(
                  TableRepositoryDiagnostics.RelationKeyIsNotAColumnReference,
                  pair.Location ?? relation.Location,
                  model.TableClassName,
                  relation.PropertyName
               )
            );

            continue;
         }

         joinedKeys.Add(new JoinedKeyPair(declaringProperty, targetProperty));
      }

      if (diagnostics.Count != beforeResolving)
         return false;

      var resolvedJoinedKeys = joinedKeys.ToImmutable();

      if (!CheckKeyPairClaims(model, relation, target, resolvedJoinedKeys, diagnostics))
         return false;

      if (!TryCheckCondition(model, relation, target, diagnostics))
         return false;

      resolved = new ResolvedRelation(
         propertyName: relation.PropertyName,
         isToMany: relation.IsToMany,
         targetDataTypeName: QualifyTypeName(target.NamespaceName, target.DataTypeName),
         joinedKeys: resolvedJoinedKeys,
         conditionBodyText: relation.Condition?.BodyText
      );

      return true;
   }

   /// <summary>
   ///    Checks a relation's resolved key pairs against the target's uniqueness claim and both tables' tenancy
   ///    claims. Applies to a relation declared through either form, because a resolved pair carries no memory of how
   ///    it was written. Returns <see langword="false" />, dropping the relation, only when a pair reaches a nullable
   ///    <c>[Unique]</c> target column (<c>PGSQL0035</c>) — the rest are warnings that report and keep the relation.
   /// </summary>
   private static bool CheckKeyPairClaims(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var beforeChecking = diagnostics.Count;

      foreach (var pair in joinedKeys)
      {
         if (!pair.TargetKey.IsUnique || !pair.TargetKey.IsNullable)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationKeyPairsAgainstNullableUniqueColumn,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               target.TableClassName,
               pair.TargetKey.PropertyName
            )
         );
      }

      if (diagnostics.Count != beforeChecking)
         return false;

      // Reaching one row is a claim, exactly like every other claim a Table definition makes, so this is a warning
      // rather than a refusal — and it only applies to a relation to one row. A relation to many rows is allowed to
      // reach several by definition.
      if (!relation.IsToMany && !PairedColumnsClaimUniqueness(target, joinedKeys))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationToOneRowMayReachSeveral,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               target.TableClassName
            )
         );
      }

      CheckTenancyPairing(model, relation, target, joinedKeys, diagnostics);

      return true;
   }

   /// <summary>
   ///    Whether the target-side columns of <paramref name="joinedKeys" /> contain something the target claims
   ///    unique — its whole primary key, or any single <c>[Unique]</c> column. A superset of a unique set is still
   ///    unique, which is why this checks containment rather than equality.
   /// </summary>
   private static bool PairedColumnsClaimUniqueness(TableDefinitionModel target, ImmutableArray<JoinedKeyPair> joinedKeys)
   {
      var pairedTargetColumns = new HashSet<PropertyDefinitionModel>(joinedKeys.Select(x => x.TargetKey));

      if (!target.PrimaryKeys.IsEmpty && target.PrimaryKeys.All(pairedTargetColumns.Contains))
         return true;

      return pairedTargetColumns.Any(x => x.IsUnique);
   }

   /// <summary>
   ///    Checks a relation definition's <c>Condition</c> against both tables' generated data types: a member touched
   ///    directly on either parameter must exist there — a mapped column, or another relation property — or the lift
   ///    into generated source would fail with no line in the developer's own code to fix. Reports every offending
   ///    member rather than only the first, because each is a separate mistake.
   /// </summary>
   private static bool TryCheckCondition(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var condition = relation.Condition;

      if (condition is null)
         return true;

      var declaringMembers = MemberNames(model);
      var targetMembers = MemberNames(target);
      var beforeChecking = diagnostics.Count;

      foreach (var memberAccess in condition.MemberAccesses)
      {
         var owner = memberAccess.IsDeclaringSide ? model : target;
         var members = memberAccess.IsDeclaringSide ? declaringMembers : targetMembers;

         if (members.Contains(memberAccess.MemberName))
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationConditionCannotBeCarried,
               memberAccess.Location ?? relation.Location,
               model.TableClassName,
               relation.PropertyName,
               owner.TableClassName,
               memberAccess.MemberName
            )
         );
      }

      return diagnostics.Count == beforeChecking;
   }

   /// <summary>Every member a table's generated data type mirrors: a mapped column, or another relation property.</summary>
   private static HashSet<string> MemberNames(TableDefinitionModel model)
   {
      var names = new HashSet<string>(StringComparer.Ordinal);

      foreach (var property in model.DataProperties)
         names.Add(property.PropertyName);

      foreach (var declaredRelation in model.Relations)
         names.Add(declaredRelation.PropertyName);

      return names;
   }

   /// <summary>
   ///    Warns on every tenancy column of either table that the joined key pairs do not pin to a tenancy column on the
   ///    other table — pair-based and direction-free, so it reads the same regardless of which side declared the
   ///    relation or which side happens to hold the foreign key. A tenancy column outside every pair warns too: the
   ///    join never touches it at all, which pins the tenant even less than pairing it against the wrong property
   ///    would. Reports once per unpinned tenancy column on either table, so a relation missing both can report twice.
   /// </summary>
   private static void CheckTenancyPairing(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      CheckTenancySide(model, relation, model, joinedKeys, isDeclaringSide: true, diagnostics);
      CheckTenancySide(model, relation, target, joinedKeys, isDeclaringSide: false, diagnostics);
   }

   /// <summary>
   ///    Checks every tenancy column of <paramref name="owner" /> — the declaring table when <paramref name="isDeclaringSide" />
   ///    is <see langword="true" />, the target otherwise — against the pair that names it, if any.
   /// </summary>
   private static void CheckTenancySide(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel owner,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      bool isDeclaringSide,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      foreach (var tenancyColumn in owner.TenancyColumns)
      {
         var pair = joinedKeys.FirstOrDefault(x => ReferenceEquals(isDeclaringSide ? x.ThisKey : x.TargetKey, tenancyColumn));
         var counterpart = pair is null ? null : isDeclaringSide ? pair.TargetKey : pair.ThisKey;

         if (counterpart is { IsTenancy: true })
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationCouldReachAcrossTenants,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               tenancyColumn.PropertyName
            )
         );
      }
   }

   /// <summary>
   ///    Warns, per <c>PGSQL0034</c>, when one table declares two relations to the same target pairing the same key
   ///    columns, where one carries a Relation condition and another does not — the unconditioned one silently
   ///    returns every kind the conditioned ones distinguish between. Only the definition form can carry a condition,
   ///    so only relations declared that way take part.
   /// </summary>
   private static void CheckForForgottenConditions(
      TableDefinitionModel model,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var byShape = new Dictionary<string, List<RelationDeclarationModel>>(StringComparer.Ordinal);

      foreach (var relation in model.Relations)
      {
         if (!relation.IsDefinitionForm || !byFullName.ContainsKey(relation.TargetClassFullName))
            continue;

         var pairs = relation.KeyPairs!.Value;

         if (pairs.Any(x => x.DeclaringPropertyName is null || x.TargetPropertyName is null))
            continue;

         var shapeKey = relation.TargetClassFullName
            + "|"
            + string.Join("|", pairs.Select(x => $"{x.DeclaringPropertyName}->{x.TargetPropertyName}").OrderBy(x => x, StringComparer.Ordinal));

         if (!byShape.TryGetValue(shapeKey, out var group))
         {
            group = [];
            byShape[shapeKey] = group;
         }

         group.Add(relation);
      }

      foreach (var group in byShape.Values)
      {
         if (group.Count < 2 || group.All(x => x.Condition is null))
            continue;

         foreach (var unconditioned in group.Where(x => x.Condition is null))
         {
            diagnostics.Add(
               Diagnostic.Create(
                  TableRepositoryDiagnostics.RelationMayResolveEveryKind,
                  unconditioned.Location,
                  model.TableClassName,
                  unconditioned.PropertyName
               )
            );
         }
      }
   }

   private static string DescribeNames(ImmutableArray<string> names)
   {
      return names.IsEmpty ? "none" : string.Join(", ", names);
   }

   /// <summary>
   ///    Whether a foreign key can join a primary key. Nullability is ignored: a nullable foreign key joining a
   ///    non-nullable primary key is the ordinary case, and is exactly what makes the relation an outer join.
   /// </summary>
   private static bool CanJoin(string foreignKeyTypeName, string primaryKeyTypeName)
   {
      return string.Equals(foreignKeyTypeName.TrimEnd('?'), primaryKeyTypeName.TrimEnd('?'), StringComparison.Ordinal);
   }

   private static string QualifyTypeName(string namespaceName, string typeName)
   {
      return string.IsNullOrWhiteSpace(namespaceName) ? $"global::{typeName}" : $"global::{namespaceName}.{typeName}";
   }
}

/// <summary>
///    A table definition together with the relations of its own that resolved. Pairing them keeps every consumer from
///    having to look one up by the other.
/// </summary>
internal sealed class ResolvedTable
{
   public ResolvedTable(TableDefinitionModel model, ImmutableArray<ResolvedRelation> relations)
   {
      Model = model;
      Relations = relations;
   }

   public TableDefinitionModel Model { get; }
   public ImmutableArray<ResolvedRelation> Relations { get; }
}

/// <summary>
///    A relation whose target and keys have been resolved, carrying everything the emitted mapping and the mirrored
///    property need.
/// </summary>
internal sealed class ResolvedRelation
{
   /// <summary>
   ///    Builds a resolved relation from pairs that already know their own declaring-side and target-side property.
   ///    Both declaration forms resolve to this shape before construction — the old attribute-argument form zips its
   ///    foreign keys against the target's primary key by cardinality first, and the definition form's pairs already
   ///    name both sides themselves — so nothing here has to know which form produced them.
   /// </summary>
   public ResolvedRelation(
      string propertyName,
      bool isToMany,
      string targetDataTypeName,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      string? conditionBodyText = null
   )
   {
      PropertyName = propertyName;
      IsToMany = isToMany;
      TargetDataTypeName = targetDataTypeName;
      JoinedKeys = joinedKeys;
      ConditionBodyText = conditionBodyText;
   }

   public string PropertyName { get; }
   public bool IsToMany { get; }

   /// <summary>The globally qualified generated data type on the other side of the relation.</summary>
   public string TargetDataTypeName { get; }

   /// <summary>The column pairs the relation joins on, in key order.</summary>
   public ImmutableArray<JoinedKeyPair> JoinedKeys { get; }

   /// <summary>
   ///    The relation definition's <c>Condition</c>, already lifted to the emitted join lambda's own parameters —
   ///    <see langword="null" /> for an ordinary relation, which states none.
   /// </summary>
   public string? ConditionBodyText { get; }
}

/// <summary>
///    One column pair a relation joins on: the property on the declaring side and the property on the target side it is
///    compared with.
/// </summary>
/// <remarks>
///    Which of the two holds the foreign key and which holds the primary key depends on the cardinality and is not
///    recorded, because nothing downstream needs to know — the join is symmetric once the pair exists.
/// </remarks>
internal sealed class JoinedKeyPair
{
   public JoinedKeyPair(PropertyDefinitionModel thisKey, PropertyDefinitionModel targetKey)
   {
      ThisKey = thisKey;
      TargetKey = targetKey;
   }

   public PropertyDefinitionModel ThisKey { get; }
   public PropertyDefinitionModel TargetKey { get; }
}
