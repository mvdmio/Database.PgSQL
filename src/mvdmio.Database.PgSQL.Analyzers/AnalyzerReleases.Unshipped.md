### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0002 | Naming | Warning | Table definition class name should end with Table
PGSQL0003 | Generation | Error | Table definition class must be partial
PGSQL0004 | Generation | Error | Table definition must declare at least one primary key
PGSQL0005 | Generation | Error | Duplicate mapped column name
PGSQL0006 | Generation | Error | Duplicate generated lookup method name
PGSQL0007 | Generation | Error | Table definition has no updatable columns
PGSQL0008 | Generation | Error | Invalid table name
PGSQL0009 | Generation | Error | Unsupported table property shape
PGSQL0010 | Generation | Error | Generated name collision
PGSQL0011 | Generation | Warning | Property type cannot be mapped by the query surface
PGSQL0012 | Generation | Error | Relation foreign key property not found
PGSQL0013 | Generation | Error | Relation foreign key type cannot match the primary key
PGSQL0014 | Generation | Error | Relation target is not a table definition
PGSQL0015 | Generation | Error | Relation to one row must be nullable
PGSQL0016 | Generation | Error | Unsupported relation property type
PGSQL0017 | Generation | Error | Unsupported relation property shape
PGSQL0018 | Generation | Error | Relation property cannot also be a column
PGSQL0019 | Generation | Error | Relation foreign key does not match the target's primary key arity
PGSQL0020 | Generation | Error | Primary key property cannot be nullable
PGSQL0021 | Generation | Error | Contradictory column nullability
