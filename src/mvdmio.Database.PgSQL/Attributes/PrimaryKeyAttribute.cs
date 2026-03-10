using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Marks the primary key property of a table definition.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PrimaryKeyAttribute : Attribute;
