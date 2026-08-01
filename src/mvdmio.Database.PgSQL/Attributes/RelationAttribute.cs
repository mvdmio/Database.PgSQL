using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Marks the annotated property as a Relation, spelling out on the property itself an intent the property's own
///    type already states.
/// </summary>
/// <remarks>
///    Optional: a property is a Relation because its type derives from
///    <see cref="mvdmio.Database.PgSQL.Relations.RelationDefinition{TDeclaring,TTarget}" />, or is a supported
///    collection of one — not because it carries this attribute. Writing it besides is accepted and changes nothing,
///    which is what lets a developer spell the intent out where they want it stated explicitly. Writing it on a
///    property whose type is not a Relation definition fails the build (<c>PGSQL0033</c>), because the attribute
///    would otherwise say something untrue.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class RelationAttribute : Attribute
{
}
