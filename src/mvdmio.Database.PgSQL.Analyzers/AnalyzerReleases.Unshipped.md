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
PGSQL0035 | Generation | Error | Relation pairs against a nullable unique column

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0012 | Generation | Error | Relation foreign key property not found
PGSQL0013 | Generation | Error | Relation foreign key type cannot match the primary key
PGSQL0019 | Generation | Error | Relation foreign key does not match the target's primary key arity
