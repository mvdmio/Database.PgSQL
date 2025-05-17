using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Attribute for specifying a column on a <see cref="DbTable" />
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public class ColumnAttribute : Attribute
{
   /// <summary>
   ///    The name of the column in the database
   /// </summary>
   public string Name { get; }

   /// <summary>
   ///    Constructor.
   /// </summary>
   public ColumnAttribute(string name)
   {
      Name = name;
   }
}