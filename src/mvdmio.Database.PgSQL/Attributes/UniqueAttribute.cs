using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Attributes;

/// <summary>
///    Marks a property as uniquely queryable for generated repository methods.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class UniqueAttribute : Attribute;
