using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    A relation whose columns have been paired but whose Relation condition has not been checked yet — everything the
///    two resolver passes need to carry between them, in one place rather than as five arguments travelling together.
/// </summary>
internal sealed class RelationCandidate
{
   public RelationCandidate(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<JoinedKeyPair> joinedKeys
   )
   {
      Model = model;
      Relation = relation;
      Target = target;
      JoinedKeys = joinedKeys;
   }

   /// <summary>The table the relation property is declared on.</summary>
   public TableDefinitionModel Model { get; }

   public RelationDeclarationModel Relation { get; }

   /// <summary>The table the relation reaches.</summary>
   public TableDefinitionModel Target { get; }

   public ImmutableArray<JoinedKeyPair> JoinedKeys { get; }

   public ResolvedRelation ToResolvedRelation()
   {
      return new ResolvedRelation(
         propertyName: Relation.PropertyName,
         isToMany: Relation.IsToMany,
         targetDataTypeName: RelationResolver.QualifyTypeName(Target.NamespaceName, Target.DataTypeName),
         joinedKeys: JoinedKeys,
         conditionBodyText: Relation.Condition?.BodyText
      );
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
   ///    Builds a resolved relation from pairs that already know their own declaring-side and target-side property —
   ///    a relation definition's <c>Keys</c> override names both sides of each pair itself.
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
