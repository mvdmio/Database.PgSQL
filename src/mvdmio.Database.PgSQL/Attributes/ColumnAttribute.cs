using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Overrides the database column name for a property.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ColumnAttribute : Attribute
{
   /// <summary>
   ///    Initializes a new instance of the <see cref="ColumnAttribute"/> class.
   /// </summary>
   /// <param name="name">The database column name.</param>
   public ColumnAttribute(string name)
   {
      Name = name;
   }

   /// <summary>
   ///    Gets the configured database column name.
   /// </summary>
   public string Name { get; }
}
