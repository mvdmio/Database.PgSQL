using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

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

   public TableDefinitionModel Model { get; }

   public RelationDeclarationModel Relation { get; }

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

internal sealed class ResolvedRelation
{
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

   public string TargetDataTypeName { get; }

   public ImmutableArray<JoinedKeyPair> JoinedKeys { get; }

   public string? ConditionBodyText { get; }
}

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
