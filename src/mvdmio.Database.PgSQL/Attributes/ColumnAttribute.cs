using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    States facts about the database column a property maps to: its name, and whether it can hold null.
/// </summary>
/// <remarks>
///    Nullability is stated here rather than through separate attributes because a standalone <c>Null</c> or
///    <c>NotNull</c> attribute would collide with <c>System.Diagnostics.CodeAnalysis.NotNullAttribute</c> and
///    <c>JetBrains.Annotations.NotNullAttribute</c>, both of which target properties as well — and a table definition
///    file already imports this namespace for <c>[Table]</c> and <c>[PrimaryKey]</c>, so importing either of those
///    alongside it would be ambiguous.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
   /// <summary>
   ///    Initializes a new instance of the <see cref="ColumnAttribute" /> class, stating nothing about the column name.
   /// </summary>
   public ColumnAttribute()
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="ColumnAttribute" /> class.
   /// </summary>
   /// <param name="name">The database column name.</param>
   public ColumnAttribute(string name)
   {
      Name = name;
   }

   /// <summary>
   ///    Gets the configured database column name, or an empty string when the attribute does not name one — in which
   ///    case the property name converted to <c>snake_case</c> is the column name.
   /// </summary>
   public string Name { get; } = string.Empty;

   /// <summary>
   ///    Gets or sets whether the column can hold null, overriding what the property's type says.
   /// </summary>
   /// <remarks>
   ///    Only needed to widen a property whose type cannot express that the column is nullable — a non-nullable
   ///    reference type over a column that permits null. Nullable is what the query surface assumes anyway, so it is
   ///    never needed to confirm a <c>Nullable&lt;T&gt;</c> or an annotated reference type.
   /// </remarks>
   public bool Null { get; set; }

   /// <summary>
   ///    Gets or sets whether the column cannot hold null, overriding what the property's type says.
   /// </summary>
   /// <remarks>
   ///    The case this exists for is a nullable-oblivious file, where the annotation that would carry the fact cannot be
   ///    written. A claim the column does not honour is not verified against the real table: a null read into a column
   ///    claimed not-null fails loudly when the row is read.
   /// </remarks>
   public bool NotNull { get; set; }
}
