using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.SourceGenerators.Attributes;

/// <summary>
///    Attribute for specifying a column on a database table. />
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
   ///   Flag indicating if this column is part of the primary key.
   ///   Multiple columns can be marked as part of the primary key.
   /// </summary>
   public bool IsPrimaryKey { get; }

   /// <summary>
   ///    Constructor.
   /// </summary>
   public ColumnAttribute(string name, bool isPrimaryKey = false)
   {
      Name = name;
      IsPrimaryKey = isPrimaryKey;
   }
}