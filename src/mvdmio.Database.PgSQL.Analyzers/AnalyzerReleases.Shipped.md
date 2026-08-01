## Release 0.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0001 | Naming | Warning | Migration class name does not follow the required convention

## Release 0.35

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
PGSQL0022 | Generation | Error | Storage claim cannot be honoured for the property's type
PGSQL0023 | Generation | Error | Property type cannot be written by a generated repository
PGSQL0024 | Generation | Warning | Storage claim has no query surface representation

## Release 0.36

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0025 | Generation | Error | Tenancy column property cannot be nullable
PGSQL0026 | Generation | Error | Tenancy column property cannot be [Generated]
PGSQL0027 | Generation | Warning | Relation could reach across tenants

## Release 0.37

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0028 | Generation | Error | Relation declaring table mismatch
PGSQL0029 | Generation | Error | Relation states no keys
PGSQL0030 | Generation | Error | Relation key is not a column reference
PGSQL0031 | Generation | Warning | Relation to one row may reach several
PGSQL0032 | Generation | Error | Relation condition cannot be carried
PGSQL0033 | Generation | Error | Relation attribute on a non-relation property
PGSQL0034 | Generation | Warning | Relation may resolve every kind
PGSQL0035 | Generation | Error | Relation key pair can both hold null

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0012 | Generation | Error | Relation foreign key property not found
PGSQL0013 | Generation | Error | Relation foreign key type cannot match the primary key
PGSQL0019 | Generation | Error | Relation foreign key does not match the target's primary key arity
