using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Attribute to decorate a Table class.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class TableAttribute : Attribute
{
   /// <summary>
   ///    The name of the table in the database.
   /// </summary>
   public string Name { get; }

   /// <summary>
   ///    The schema of the table in the database. Defaults to "public".
   /// </summary>
   public string Schema { get; }

   /// <summary>
   ///    Constructor.
   /// </summary>
   public TableAttribute(string name, string schema = "public")
   {
      Name = name;
      Schema = schema;
   }
}