using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Emits the relation properties a generated data type mirrors from its table definition.
/// </summary>
/// <remarks>
///    Emitted from the stage that has resolved every relation, as a further part of the already-partial data type,
///    because the far end of a relation is another table's generated data type and no single table knows about it. The
///    create and update command types get nothing: mutation shapes stay as flat as the table they write to.
/// </remarks>
internal static class TableRelationsSourceBuilder
{
   public static string Build(ResolvedTable table)
   {
      var model = table.Model;
      var builder = new StringBuilder();
      builder.AppendLine("#nullable enable");

      if (!string.IsNullOrWhiteSpace(model.NamespaceName))
      {
         builder.AppendLine();
         builder.AppendLine($"namespace {model.NamespaceName};");
      }

      builder.AppendLine();
      builder.AppendLine($"{model.Accessibility} partial class {model.DataTypeName}");
      builder.AppendLine("{");

      foreach (var relation in table.Relations)
      {
         if (relation.IsToMany)
            builder.AppendLine($"   public global::System.Collections.Generic.List<{relation.TargetDataTypeName}> {relation.PropertyName} {{ get; set; }} = new();");
         else
            builder.AppendLine($"   public {relation.TargetDataTypeName}? {relation.PropertyName} {{ get; set; }}");
      }

      builder.AppendLine("}");

      return builder.ToString();
   }
}
