using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableRepositorySourceBuilder
{
   /// <summary>
   ///    The name of the primary-key lookup on every generated repository. Fixed rather than derived from the key's
   ///    properties, so a reader never has to discover what it is called for a given table — which only holds if it is
   ///    universal, so a single-column key gets this name too.
   /// </summary>
   public const string PRIMARY_KEY_LOOKUP_METHOD_NAME = "GetByPrimaryKeyAsync";

   /// <summary>The delete that mirrors <see cref="PRIMARY_KEY_LOOKUP_METHOD_NAME" />.</summary>
   public const string PRIMARY_KEY_DELETE_METHOD_NAME = "DeleteByPrimaryKeyAsync";

   /// <summary>The lookup a <c>[Unique]</c> property gets, which stays named after the property.</summary>
   public static string LookupMethodName(PropertyDefinitionModel property)
   {
      return $"GetBy{property.PropertyName}Async";
   }

   /// <summary>The delete a <c>[Unique]</c> property gets, which stays named after the property.</summary>
   public static string DeleteMethodName(PropertyDefinitionModel property)
   {
      return $"DeleteBy{property.PropertyName}Async";
   }

   public static string Build(TableDefinitionModel model)
   {
      var builder = new StringBuilder();
      builder.AppendLine("#nullable enable");
      builder.AppendLine("using System;");
      builder.AppendLine("using System.Collections.Generic;");
      builder.AppendLine("using System.Linq;");
      builder.AppendLine("using System.Threading;");
      builder.AppendLine("using System.Threading.Tasks;");

      if (!string.IsNullOrWhiteSpace(model.NamespaceName))
      {
         builder.AppendLine();
         builder.AppendLine($"namespace {model.NamespaceName};");
      }

      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.DataTypeName, model.DataProperties, DtoKind.Data);
      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.CreateCommandTypeName, model.CreateProperties, DtoKind.Command);
      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.UpdateCommandTypeName, model.UpdateProperties, DtoKind.Command);
      builder.AppendLine();
      AppendRepositoryInterface(builder, model);
      builder.AppendLine();
      AppendRepository(builder, model);

      return builder.ToString();
   }

   /// <summary>
   ///    Which of the three generated types is being emitted, which is the whole of what differs between them. The two
   ///    rules below both read off it, so a type cannot be given one half of a kind's behaviour and not the other.
   /// </summary>
   private enum DtoKind
   {
      /// <summary>
      ///    The type Dapper materializes a row into, replacing a hand-written row record. A caller may not assign a
      ///    column the database populates, and nothing on it can be <c>required</c>, because Dapper builds it through a
      ///    parameterless constructor.
      /// </summary>
      Data,

      /// <summary>
      ///    A create or update command a caller builds by hand. Every column stays assignable — an update addresses its
      ///    row by a primary key that may itself be generated, so the caller has to be able to supply it — and the
      ///    tenancy columns are <c>required</c>.
      /// </summary>
      Command
   }

   /// <remarks>
   ///    <c>required</c> and <c>init</c> are never mirrored, whatever the table definition declares — these types have
   ///    no constructor that could satisfy <c>required</c>, and every column but a generated one has to stay assignable
   ///    for a command to be built. A definition pairing <c>required … { get; init; }</c> with
   ///    <c>{ get; private set; }</c> therefore keeps the half that guards a database-populated column and loses the
   ///    other.
   ///    <para>
   ///       The one exception is a tenancy column on a <see cref="DtoKind.Command" />. It is not mirrored from anything
   ///       the table definition declares about the column itself — <c>[Column(Tenancy = true)]</c> carries no
   ///       <c>required</c> or <c>init</c> claim of its own — but added, so a construction site that omits the tenant
   ///       fails to build rather than writing a row under a default value.
   ///    </para>
   /// </remarks>
   private static void AppendDto(
      StringBuilder builder,
      string accessibility,
      string typeName,
      ImmutableArray<PropertyDefinitionModel> properties,
      DtoKind kind
   )
   {
      builder.AppendLine($"{accessibility} partial class {typeName}");
      builder.AppendLine("{");

      foreach (var property in properties)
      {
         var setter = kind is DtoKind.Data && property.IsGenerated ? "private set;" : "set;";
         var requiredKeyword = kind is DtoKind.Command && property.IsTenancy ? "required " : string.Empty;

         builder.Append("   public ")
            .Append(requiredKeyword)
            .Append(property.TypeName)
            .Append(' ')
            .Append(property.PropertyName)
            .Append(" { get; ")
            .Append(setter)
            .Append(" }");

         if (property.RequiresNullForgivingInitializer && requiredKeyword.Length == 0)
            builder.Append(" = default!;");

         builder.AppendLine();
      }

      builder.AppendLine("}");
   }

   private static void AppendRepositoryInterface(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"{model.Accessibility} partial interface {model.RepositoryInterfaceTypeName}");
      builder.AppendLine("{");
      builder.AppendLine($"   Task<{model.DataTypeName}> CreateAsync({model.CreateCommandTypeName} data, CancellationToken ct = default);");
      builder.AppendLine($"   Task<IEnumerable<{model.DataTypeName}>> GetAllAsync({ParameterListPrefix(model.TenancyConstraint)}CancellationToken ct = default);");
      builder.AppendLine($"   Task<{model.DataTypeName}?> {PRIMARY_KEY_LOOKUP_METHOD_NAME}({ParameterListPrefix(model.PrimaryKeyConstraint)}CancellationToken ct = default);");

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine($"   Task<{model.DataTypeName}?> {LookupMethodName(property)}({ParameterListPrefix(model.LookupConstraint(property))}CancellationToken ct = default);");
      }

      builder.AppendLine($"   Task<{model.DataTypeName}> UpdateAsync({model.UpdateCommandTypeName} data, CancellationToken ct = default);");
      builder.AppendLine($"   Task<bool> {PRIMARY_KEY_DELETE_METHOD_NAME}({ParameterListPrefix(model.PrimaryKeyConstraint)}CancellationToken ct = default);");

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine($"   Task<bool> {DeleteMethodName(property)}({ParameterListPrefix(model.LookupConstraint(property))}CancellationToken ct = default);");
      }

      builder.AppendLine($"   IQueryable<{model.DataTypeName}> Query({ParameterListPrefix(model.TenancyConstraint)}TimeSpan? commandTimeout = null);");
      builder.AppendLine("}");
   }

   private static void AppendRepository(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"{model.Accessibility} partial class {model.RepositoryTypeName} : {model.RepositoryInterfaceTypeName}");
      builder.AppendLine("{");
      builder.AppendLine("   private readonly global::mvdmio.Database.PgSQL.DatabaseConnection _db;");
      builder.AppendLine();
      builder.AppendLine($"   public {model.RepositoryTypeName}(global::mvdmio.Database.PgSQL.DatabaseConnection db)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(db);");
      builder.AppendLine("      _db = db;");
      builder.AppendLine("   }");
      builder.AppendLine();

      AppendCreateMethod(builder, model);
      builder.AppendLine();
      AppendGetAllMethod(builder, model);
      builder.AppendLine();
      AppendGetByPrimaryKeyMethod(builder, model);

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine();
         AppendGetByMethod(builder, model, property);
      }

      builder.AppendLine();
      AppendUpdateMethod(builder, model);
      builder.AppendLine();
      AppendDeleteByPrimaryKeyMethod(builder, model);

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine();
         AppendDeleteByMethod(builder, model, property);
      }

      builder.AppendLine();
      AppendQueryMethod(builder, model);

      builder.AppendLine("}");
   }

   private static void AppendQueryMethod(StringBuilder builder, TableDefinitionModel model)
   {
      var constraint = model.TenancyConstraint;
      builder.AppendLine($"   public IQueryable<{model.DataTypeName}> Query({ParameterListPrefix(constraint)}TimeSpan? commandTimeout = null)");
      builder.AppendLine("   {");

      if (constraint.IsEmpty)
      {
         builder.AppendLine($"      return _db.Linq.Query<{model.DataTypeName}>(commandTimeout);");
      }
      else
      {
         var predicate = string.Join(" && ", constraint.InStatementOrder.Select(x => $"x.{x.PropertyName} == {x.ParameterName}"));
         builder.AppendLine($"      return _db.Linq.Query<{model.DataTypeName}>(commandTimeout).Where(x => {predicate});");
      }

      builder.AppendLine("   }");
   }

   private static void AppendCreateMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}> CreateAsync({model.CreateCommandTypeName} data, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(data);");
      builder.AppendLine();
      builder.AppendLine($"      return await _db.Dapper.QuerySingleAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildCreateSql(model));

      if (model.CreateProperties.Length == 0)
      {
         builder.AppendLine(",");
         builder.AppendLine("         ct: ct");
      }
      else
      {
         builder.AppendLine(",");
         AppendParameterDictionary(builder, CommandBindings(model.CreateProperties, "data"), 9);
         builder.AppendLine(",");
         builder.AppendLine("         ct: ct");
      }

      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendGetAllMethod(StringBuilder builder, TableDefinitionModel model)
   {
      var constraint = model.TenancyConstraint;
      builder.AppendLine($"   public async Task<IEnumerable<{model.DataTypeName}>> GetAllAsync({ParameterListPrefix(constraint)}CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine($"      return await _db.Dapper.QueryAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildGetAllSql(model));
      builder.AppendLine(",");

      if (constraint.IsEmpty)
      {
         builder.AppendLine("         ct: ct");
      }
      else
      {
         AppendParameterDictionary(builder, ParameterBindings(constraint.InStatementOrder), 9);
         builder.AppendLine(",");
         builder.AppendLine("         ct: ct");
      }

      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendGetByPrimaryKeyMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}?> {PRIMARY_KEY_LOOKUP_METHOD_NAME}({ParameterListPrefix(model.PrimaryKeyConstraint)}CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine($"      return await _db.Dapper.QuerySingleOrDefaultAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildGetByPrimaryKeySql(model));
      builder.AppendLine(",");
      AppendParameterDictionary(builder, ParameterBindings(model.PrimaryKeyConstraint.InStatementOrder), 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendDeleteByPrimaryKeyMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<bool> {PRIMARY_KEY_DELETE_METHOD_NAME}({ParameterListPrefix(model.PrimaryKeyConstraint)}CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      var affectedRows = await _db.Dapper.ExecuteAsync(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildDeleteByPrimaryKeySql(model));
      builder.AppendLine(",");
      AppendParameterDictionary(builder, ParameterBindings(model.PrimaryKeyConstraint.InStatementOrder), 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine();
      builder.AppendLine("      return affectedRows > 0;");
      builder.AppendLine("   }");
   }

   private static void AppendGetByMethod(StringBuilder builder, TableDefinitionModel model, PropertyDefinitionModel property)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}?> {LookupMethodName(property)}({ParameterListPrefix(model.LookupConstraint(property))}CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine($"      return await _db.Dapper.QuerySingleOrDefaultAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildGetBySql(model, property));
      builder.AppendLine(",");
      AppendParameterDictionary(builder, ParameterBindings(model.LookupConstraint(property).InStatementOrder), 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendUpdateMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}> UpdateAsync({model.UpdateCommandTypeName} data, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(data);");
      builder.AppendLine();
      builder.AppendLine($"      return await _db.Dapper.QuerySingleAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildUpdateSql(model));
      builder.AppendLine(",");
      AppendParameterDictionary(builder, CommandBindings(model.UpdateProperties, "data"), 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendDeleteByMethod(StringBuilder builder, TableDefinitionModel model, PropertyDefinitionModel property)
   {
      builder.AppendLine($"   public async Task<bool> {DeleteMethodName(property)}({ParameterListPrefix(model.LookupConstraint(property))}CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      var affectedRows = await _db.Dapper.ExecuteAsync(");
      AppendSqlLiteral(builder, 9, TableRepositorySqlStatements.BuildDeleteBySql(model, property));
      builder.AppendLine(",");
      AppendParameterDictionary(builder, ParameterBindings(model.LookupConstraint(property).InStatementOrder), 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine();
      builder.AppendLine("      return affectedRows > 0;");
      builder.AppendLine("   }");
   }

   /// <summary>
   ///    One parameter per column <paramref name="constraint" /> names, in the order a signature takes them — every
   ///    tenancy column first and in declaration order, then whatever the member addresses its row by — each with the
   ///    trailing comma the optional parameter after it needs. Empty when the constraint is, so a member that
   ///    constrains nothing keeps the signature it has today.
   /// </summary>
   /// <remarks>
   ///    Every generated signature is built here, so all four read alike and none of them can disagree with the
   ///    statement it issues about which columns are constrained. Which columns those are is the model's question.
   /// </remarks>
   private static string ParameterListPrefix(ConstrainedColumns constraint)
   {
      return string.Join(string.Empty, constraint.InParameterOrder.Select(x => $"{x.TypeName} {x.ParameterName}, "));
   }

   /// <summary>
   ///    The parameter dictionary a Dapper call takes: one binding per parameter, each the name the SQL names it by and
   ///    the C# expression supplying its value.
   /// </summary>
   private static void AppendParameterDictionary(StringBuilder builder, IEnumerable<(string Name, string Value)> bindings, int indentation)
   {
      builder.Append(' ', indentation).AppendLine("parameters: new Dictionary<string, object?>");
      builder.Append(' ', indentation).AppendLine("{");

      foreach (var (name, value) in bindings)
      {
         builder.Append(' ', indentation + 3)
            .Append("[")
            .Append('"')
            .Append(name)
            .Append('"')
            .Append("] = ")
            .Append(value)
            .AppendLine(",");
      }

      builder.Append(' ', indentation).Append('}');
   }

   /// <summary>Bindings for a statement whose values come off a command object, named by property.</summary>
   private static IEnumerable<(string Name, string Value)> CommandBindings(ImmutableArray<PropertyDefinitionModel> properties, string valueSource)
   {
      return properties.Select(x => (x.PropertyName, BindingExpression(x, $"{valueSource}.{x.PropertyName}")));
   }

   /// <summary>Bindings for a statement whose values come off method parameters, named by parameter.</summary>
   private static IEnumerable<(string Name, string Value)> ParameterBindings(IEnumerable<PropertyDefinitionModel> properties)
   {
      return properties.Select(x => (x.ParameterName, BindingExpression(x, x.ParameterName)));
   }

   /// <summary>
   ///    What one column's value is bound from, with its storage claim applied.
   /// </summary>
   /// <remarks>
   ///    The only place a claim reaches the wire on the Dapper surface, and it does so through two mechanisms rather than
   ///    a registry: the value is converted where its CLR type has to change — an enum to the text of its member name, a
   ///    narrow integer widened to one the driver can write — and the parameter type is stated where the value stands as
   ///    it is and only the driver's inference is wrong, which is the <c>jsonb</c> case. Nothing is registered, so nothing
   ///    can be registered twice or disagree with the mapping the same claim produces for the query surface.
   /// </remarks>
   private static string BindingExpression(PropertyDefinitionModel property, string valueExpression)
   {
      var storage = property.Storage;
      var nullability = property.TypeCanHoldNull ? "?" : string.Empty;

      var value = storage.Conversion switch
      {
         { IsEnumMemberName: true } => $"{valueExpression}{nullability}.ToString()",
         { } cast => $"({cast.StoredTypeName}{nullability}){valueExpression}",
         null => valueExpression
      };

      if (storage.BoundAs is null)
         return value;

      return $"new global::mvdmio.Database.PgSQL.Dapper.QueryParameters.TypedQueryParameter({value}, {ColumnStorage.CLAIM_TYPE_FULL_NAME}.{storage.BoundAs})";
   }

   private static void AppendSqlLiteral(StringBuilder builder, int indentation, string sql)
   {
      builder.Append(' ', indentation).AppendLine("sql: \"\"\"");
      foreach (var line in sql.Split('\n'))
      {
         builder.Append(' ', indentation).AppendLine(line.TrimEnd('\r'));
      }

      builder.Append(' ', indentation).Append("\"\"\"");
   }

}
