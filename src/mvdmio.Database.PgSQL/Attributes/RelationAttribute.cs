using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Declares that the annotated property is a relation to another table definition rather than a column.
/// </summary>
/// <remarks>
///    The property's type names the other table definition, and states the cardinality by whether it is a collection:
///    a single table definition is a relation to one row, resolved through a foreign key on the declaring type, and a
///    collection of one is a relation to many rows, resolved through a foreign key on the target type. The other side
///    of the relation is always the target's primary key, so the foreign key is the only thing left to name — pass it
///    with <c>nameof</c> so a rename is caught at build time. A target whose primary key is composite takes one
///    foreign-key property per key member, paired positionally against it.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class RelationAttribute : Attribute
{
   /// <summary>
   ///    Initializes a new instance of the <see cref="RelationAttribute" /> class.
   /// </summary>
   /// <param name="foreignKeyPropertyNames">
   ///    The names of the properties holding the foreign key that resolves the relation, one per member of the target's
   ///    primary key and in that key's order. They live on the declaring type for a relation to one row, and on the
   ///    target type for a relation to many.
   /// </param>
   public RelationAttribute(params string[] foreignKeyPropertyNames)
   {
      ForeignKeyPropertyNames = foreignKeyPropertyNames;
   }

   /// <summary>
   ///    Gets the names of the properties holding the foreign key that resolves the relation, in the order they are
   ///    paired against the target's primary key.
   /// </summary>
   public IReadOnlyList<string> ForeignKeyPropertyNames { get; }
}
