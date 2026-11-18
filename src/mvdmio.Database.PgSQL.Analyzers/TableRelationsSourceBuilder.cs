using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

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
