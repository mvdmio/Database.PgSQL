using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Declares the database table mapped by a table definition class.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute : Attribute
{
   /// <summary>
   ///    Initializes a new instance of the <see cref="TableAttribute"/> class.
   /// </summary>
   /// <param name="name">The table name. Supports <c>table</c> or <c>schema.table</c>.</param>
   public TableAttribute(string name)
   {
      Name = name;
   }

   /// <summary>
   ///    Gets the configured table name.
   /// </summary>
   public string Name { get; }
}
