### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PGSQL0036 | Migration | Error | Migration defines PostDeployAsync together with UpAsync
PGSQL0037 | Migration | Error | Migration defines both UpAsync and PreDeployAsync
PGSQL0038 | Migration | Error | Migration defines neither UpAsync nor PreDeployAsync
PGSQL0039 | Usage | Error | Migration runs a statement PostgreSQL refuses inside a transaction
