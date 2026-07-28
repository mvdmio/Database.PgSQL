using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Marks a property as part of the primary key of a table definition.
/// </summary>
/// <remarks>
///    Two or more properties may carry it, which declares a composite primary key. The order they are declared in is the
///    key order, and that order is contract: it fixes the parameter order of the generated primary-key lookup and delete,
///    and the order a relation's foreign-key properties are paired against the target's key. A nullable property cannot
///    carry it.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute;
