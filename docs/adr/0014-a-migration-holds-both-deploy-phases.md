# A migration holds both deploy phases, with one history row per part

A deploy pipeline runs migrations as steps of their own: one before the new application version starts serving, and one after. Some changes need work at both points. Replacing an index builds the new index before the deploy and drops the old one after it, because the old version still uses the old index until the deploy finishes.

We decided that one **Migration** holds both parts. `IDbMigration` has `PreDeployAsync`, which defaults to calling `UpAsync`, and an optional `PostDeployAsync`. The two parts of one change belong together, so they are written, reviewed, and deleted together. Each part writes its own history row, told apart by the `phase` column, so the migrator knows the pre-deploy part ran while the post-deploy part still waits.

A **Post-deploy part** runs only in a post-deploy run or an `All` run, and only once its own **Pre-deploy part** counts as present. A pre-deploy run never runs one, even one left from an earlier release. The migrator cannot see whether the deploy between the two runs succeeded. If release N's deploy is rolled back, release N's post-deploy part would drop what the still-serving older version uses. A post-deploy run is the only point where the pipeline tells the migrator the deploy is done.

## Considered options

- **Both parts in one migration, one row per part (chosen).** The pair stays in one file, and the pending rules of [ADR 0002](0002-per-scope-pending-migrations.md) apply to each part with one extra condition on the post-deploy part.
- **One phase per migration, a pair linked by naming.** Rejected. The two halves of one change live in two files with two identifiers, and nothing ties them together when one is edited or deleted.
- **Mark the part in the `name` column with a `_PostDeploy` suffix.** Rejected. A part's identity would rest on a text suffix, and the unique index could no longer enforce one row per part.
- **Let a pre-deploy run also run post-deploy parts left from earlier runs.** Rejected for the rolled-back deploy above.

## Consequences

- An older library version never runs a post-deploy part, and counts a migration as run once any row for its identifier exists. Only an upgraded application runs post-deploy parts.
- A migration that already ran and later gains `PostDeployAsync` gets that part run at the next post-deploy run.
- A schema pulled between the two runs folds in a pending post-deploy part without its effect, as ADR 0002 records for out-of-order migrations. Pull after the post-deploy run.
