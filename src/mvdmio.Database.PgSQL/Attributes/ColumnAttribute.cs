using JetBrains.Annotations;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    States facts about the database column a property maps to: its name, whether it can hold null, and how the value
///    is stored.
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
   ///    written. Nothing verifies the claim against the real table, and a column that does hold null is not caught when
   ///    the row is read — the null arrives in the property regardless of its type. What a wrong claim costs is rows: an
   ///    inequality over the column no longer matches the ones where it is null. Set this because the table says
   ///    <c>NOT NULL</c>, not because a value is usually present.
   /// </remarks>
   public bool NotNull { get; set; }

   /// <summary>
   ///    Gets or sets how the column's value is stored, as the PostgreSQL type the value is bound as.
   /// </summary>
   /// <remarks>
   ///    Only needed where the property's own type does not settle it. An enum is stored as the text of its member name
   ///    without this; set it to <see cref="NpgsqlDbType.Smallint" />, <see cref="NpgsqlDbType.Integer" /> or
   ///    <see cref="NpgsqlDbType.Bigint" /> to store the underlying number instead. A <c>string</c> holding JSON needs
   ///    <see cref="NpgsqlDbType.Jsonb" /> or <see cref="NpgsqlDbType.Json" />, because PostgreSQL will not cast text to
   ///    either one implicitly.
   ///    <para>
   ///       Stated per column rather than per type, so two columns of the same enum can be stored differently. The claim
   ///       feeds the generated parameter binding and the query surface mapping both, so the two cannot disagree about
   ///       the column. Nothing verifies it against the real table.
   ///    </para>
   ///    <para>
   ///       Permitted rather than curated: a claim the library has no test for is still carried, and only a documented
   ///       subset is known to round-trip. A claim the query surface cannot represent warns at build time and is honoured
   ///       on the Dapper surface alone.
   ///    </para>
   /// </remarks>
   public NpgsqlDbType StoredAs { get; set; }
}
