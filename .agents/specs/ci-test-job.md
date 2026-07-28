# Run the tests in CI before publishing

Status: ready-for-agent

## Problem Statement

The publish pipeline pushes packages to NuGet.org without ever running a test. It triggers on pushes to `main` that
touch the library sources, restores, builds, packs the tool, and pushes — no test run, no formatting check. The
reference documentation states the position outright and asks contributors to run the tests locally before merging.

That holds only as long as whoever pushes remembers. A published package cannot be withdrawn, so the first signal that
a change broke something would be a consumer reporting it.

The exposure grew with the **query surface**, whose behaviour is only observable against a real PostgreSQL, and grew
again with the OData conformance suite, whose whole purpose is catching regressions that reading the code would not
reveal. Both are integration suites that need containers, and neither runs anywhere except a developer's machine.

Separately, `dotnet format --verify-no-changes` exits non-zero today — 29 distinct source locations violate the
repository's own documented conventions — so drift has already accumulated unchecked.

## Solution

Keep the pipeline's purpose exactly as it is: **it exists to publish.** Tests become the safeguard on that act rather
than an independent goal. A change that does not warrant a publish still triggers nothing.

The single workflow becomes three sequential jobs — `build` → `test` → `publish` — each gating the next. `build`
verifies formatting, compiles the solution once, and produces the packages. `test` runs all four test suites against
the assemblies `build` produced. `publish` pushes the package artifact that those tests gated. A red format check or a
failing test means no package reaches the feed.

The 29 existing format violations are cleared first, so the gate is green from the moment it lands, and `CLAUDE.md`
records that the check must pass before any change is considered finished.

## User Stories

1. As a library maintainer, I want the test suites to run before a package is pushed, so that a regression is caught
   before it reaches consumers rather than after.
2. As a library maintainer, I want a failing test to abort the publish, so that I cannot ship a package I could not
   withdraw.
3. As a library maintainer, I want the pipeline to keep triggering only on publish-worthy changes, so that its purpose
   stays legible and runner time is not spent on pushes that ship nothing.
4. As a library maintainer, I want a version bump alone to be able to trigger the pipeline, so that a re-release does
   not require an unrelated source edit to wake the workflow up.
5. As a library maintainer, I want the integration suites to run in CI despite needing containers, so that the
   behaviour that is only observable against a real PostgreSQL is actually verified.
6. As a library maintainer, I want the OData conformance suite to run in CI, so that the **translation boundary** it
   pins stays pinned.
7. As a library maintainer, I want the cheap suites to report before the container-backed ones, so that a compile-level
   or logic-level break surfaces in seconds instead of after images are pulled.
8. As a library maintainer, I want the solution compiled exactly once per run, so that the assemblies the tests execute
   are the same assemblies that go into the package.
9. As a library maintainer, I want `publish` to push the package artifact produced by `build`, so that what ships is
   provably what was gated.
10. As a library maintainer, I want formatting verified before anything compiles, so that the cheapest possible failure
    happens first.
11. As a library maintainer, I want `dotnet format --verify-no-changes` to exit zero on a clean checkout, so that the
    gate reports real drift rather than a permanent backlog.
12. As a library maintainer, I want the existing 29 violations fixed to match the documented conventions rather than
    the conventions relaxed to match the code, so that `conventions.md` remains true.
13. As a library maintainer, I want the contradictory duplicate `const_field` naming rule removed from
    `.editorconfig`, so that the configuration stops disagreeing with itself.
14. As a library maintainer, I want the cleanup to change no public surface, so that no version bump is forced by
    housekeeping.
15. As a library maintainer, I want two publishes never to run concurrently, so that racing pushes cannot interleave
    uploads.
16. As a library maintainer, I want an in-flight publish never cancelled by a newer push, so that a run is never killed
    halfway through an upload.
17. As a library maintainer, I want every job to have a timeout, so that a hung container fails in minutes instead of
    burning the six-hour job limit.
18. As a library maintainer, I want the whole pipeline on `ubuntu-latest`, so that no job is pinned to a runner image
    that will eventually be retired.
19. As a library maintainer, I want the `nuget.exe` dependency gone, so that the reason the old pin existed is removed
    rather than worked around.
20. As a library maintainer, I want `workflow_dispatch` retained, so that I can re-run the pipeline on demand after a
    transient container failure without pushing an empty commit.
21. As a library maintainer, I want a re-run of an unchanged version to be a harmless no-op, so that a retry never
    fails on an already-published package.
22. As a library maintainer, I want the escape hatch for skipping the pipeline written down with the correct keyword,
    so that a docs-only push to a triggering path can opt out without guesswork.
23. As a library maintainer, I want `.agents/refs/dependencies.md` to describe the pipeline as it actually is, so that
    the claim that CI does not run tests stops being documented as current.
24. As a contributor, I want a red pipeline to tell me which stage failed — format, test, or push — so that I know
    where to look without reading the log from the top.
25. As a contributor, I want the CI sequence to mirror the documented local sequence of format → build → test, so that
    a green local run predicts a green pipeline.
26. As a coding agent working in this repository, I want `CLAUDE.md` to state that the format check must exit zero
    before a change is finished, so that I clear drift before it reaches the pipeline rather than after.
27. As a coding agent, I want to know that the format gate now blocks publishing, so that I treat a format failure as a
    release blocker rather than a style nit.
28. As a consumer of the package, I want every published version to have passed the full suite, so that upgrading is
    not a gamble on whether anyone ran the tests.
29. As a consumer of the package, I want the **query surface** and OData conformance behaviour verified per release, so
    that a version bump does not silently move the **translation boundary**.

## Implementation Decisions

### Pipeline purpose and triggers

- The workflow's purpose is unchanged: **it exists to publish.** Tests are the safeguard on publishing, not an
  independent goal. This is why the path filter is kept rather than widened to all changes.
- Triggers stay `push` to `main` plus `workflow_dispatch`. The path filter gains `Directory.Build.props` alongside
  `src/**`, because `PgSqlVersion` lives there and a version bump is by definition a publish-worthy change that the
  current filter would miss.
- A change touching only test projects therefore triggers nothing — deliberately. Those changes are still covered, at
  the moment it matters, by the next run that does publish.
- **No pull-request build.** Work lands by direct push to `main`, so a `pull_request` trigger would never fire.
- A `concurrency` group scoped to the workflow serializes runs with `cancel-in-progress: false`. The false is the
  important half: an in-flight publish must never be cancelled mid-upload.

### Job structure — three jobs, build once

Replace the single job with `build` → `test` → `publish`, each declaring `needs:` on the previous. All three run on
`ubuntu-latest`, all three install SDKs 8, 9 and 10 via `actions/setup-dotnet` (the library and tool multi-target
`net8.0;net9.0;net10.0`, so `build` genuinely needs all three; keeping the block uniform means nobody has to re-derive
which job needs what), and all three carry a `timeout-minutes`.

**`build`** — the source gate and the only compile:

1. `dotnet format --verify-no-changes` over the solution, as the first step after SDK setup. A static check on source
   belongs with the compile gate and fails in roughly fifteen seconds.
2. Restore, then build the solution in Release. The library sets `GeneratePackageOnBuild`, so this produces its
   `.nupkg`/`.snupkg` as a side effect of building.
3. Pack the tool project in Release without rebuilding — it sets `PackAsTool`, so this produces the `db` tool package.
4. Upload two artifacts: the Release build output (`bin` **and** `obj`), and the packages.

The build output artifact must include `obj`, not just `bin`. `--no-build` still makes MSBuild evaluate each project,
and that evaluation reads the generated files under `obj`.

**`test`** — runs against what `build` produced:

1. Download the build-output artifact.
2. Run `dotnet restore`. This is required even though nothing needs compiling: `Microsoft.NET.Test.Sdk` and
   `xunit.runner.visualstudio` contribute MSBuild `.props`/`.targets` that are imported from the NuGet package cache,
   so evaluation fails without a populated cache. The artifact saves the **compile**, not the restore.
3. Two test steps, in order — the unit and analyzer suites first, then the two container-backed integration suites.
   Both use `--no-build` in Release. Splitting them costs nothing in total time and means a logic-level break reports
   before any container image is pulled.

**`publish`** — pushes the gated artifact:

1. Download the packages artifact. No checkout is needed.
2. Push with `dotnet nuget push` against NuGet.org, using `--skip-duplicate` so a re-run against an unchanged
   `PgSqlVersion` is a harmless no-op. Pushing the `.nupkg` also uploads a sibling `.snupkg`, matching the existing
   symbol-package behaviour.

### Replacing `nuget.exe`

`nuget/setup-nuget` and the `nuget push` step are removed in favour of `dotnet nuget push`. The old workflow pinned
`ubuntu-22.04` solely because `nuget.exe` runs under Mono and fails on Ubuntu 24.04 with `mono: not found`
(NuGet/setup-nuget issue 168, still open). Since `ubuntu-latest` is now 24.04, moving everything to `ubuntu-latest`
requires removing the Mono dependency rather than working around it.

This is a deliberate, called-out widening of the original idea, which listed changes to the publish step as out of
scope. The packaging **outcome** is unchanged — same packages, same feed, same duplicate-skipping behaviour — only the
tool that performs the upload changes.

### Container concurrency

The integration suite opens a shared `postgres:18` assembly fixture plus three per-test-class containers, and the OData
suite opens its own — a peak of roughly three concurrent containers. GitHub-hosted `ubuntu` runners ship Docker and
provide four vCPUs and 16 GB, which is ample; macOS and Windows runners have no Docker, which is a further reason the
pipeline stays on Linux. No test-parallelism knobs are changed: the suites already run concurrently under a single
`dotnet test` invocation and complete in about fifty seconds locally.

### Format cleanup, done first

`dotnet format --verify-no-changes` currently exits 2. Split by subcommand: `whitespace` is **clean** (line endings and
indentation are fine — the idea file's suspicion that the formatter and repository disagree about line endings is not
borne out), `style` reports 39 warnings and `analyzers` 2. Deduplicated across target frameworks that is **29 distinct
source locations** — 5 in `src`, 24 in test projects.

They fall into four groups:

- **Constants not in UPPER_SNAKE_CASE** — the large majority. Local `const` declarations and `const` fields in
  camelCase, some with an `_` prefix. `conventions.md` already specifies UPPER_SNAKE_CASE for constants, so the code
  drifted and the rule did not. These are renamed.
- **One `internal` async method without the `Async` suffix** — the standard .NET `IAsyncDisposable` core-method name.
  Renaming it would fight the framework idiom, so it takes a targeted suppression with a comment explaining why. It is
  `internal`, so nothing public is involved either way.
- **One unused test parameter** — on a hand-written fake that implements a generated repository interface, so the
  parameter is part of the member it implements and **cannot** simply be deleted. It takes a targeted suppression.
- **One xUnit cancellation-token warning** — the call is given the ambient test cancellation token, matching how the
  rest of the integration suite already threads it.

While in `.editorconfig`, remove the dead duplicate `const_field` naming rule. Two rules currently target
`const_field` with conflicting styles; first match wins, so the later one is unreachable and only creates the
impression that the file permits both.

**Nothing public changes.** The five `src` locations are four local `const` renames and one `internal` method
suppression — no public surface, no behavioural change — so `PgSqlVersion` is **not** bumped by this work.

This is a deliberate exception to the standing instruction in `CLAUDE.md` to bump `PgSqlVersion` whenever the library
or tool changes. `src` files are edited here, but only local constants inside method bodies and one suppression
comment; nothing a consumer can reference, call or observe is affected. An implementing agent should **not** bump the
version to satisfy that rule. The same reasoning covers `README.md`: it documents public behaviour, none of which
moves.

### Documentation

- `.agents/refs/dependencies.md` — rewrite the CI/CD section. It currently documents the single job and states that
  the pipeline does not run tests. It must describe the three-job structure, the trigger paths, the concurrency
  behaviour, and the `[skip ci]` escape hatch.
- `CLAUDE.md` — strengthen the existing format → build → test instruction to state that
  `dotnet format --verify-no-changes` must exit zero before a change is finished, and that CI enforces this as a
  publish gate.
- `README.md` — reviewed and updated only if it makes a claim this change invalidates; it currently has no CI section
  and no status badge, and none is added.
- **No ADR and no `CONTEXT.md` term.** `CONTEXT.md` is the glossary of the data-access domain — migrations, table
  definitions, the query surface — and CI vocabulary is not part of it. The existing ADRs all pin library design
  decisions; this is delivery infrastructure with no bearing on how the library behaves.

### Escape hatch

Document `[skip ci]` as the supported way to bypass the pipeline on a push that touches a triggering path but ships
nothing. GitHub honours `[skip ci]`, `[ci skip]`, `[no ci]`, `[skip actions]` and `[actions skip]` in the commit
message for `push` events, plus a `skip-checks: true` trailer. Recording the exact keyword matters because the
plausible-looking variants do not work.

## Testing Decisions

A good test here asserts external, observable behaviour and nothing about how it is achieved. For this change that
principle points somewhere slightly unusual: the deliverable is a workflow definition and a set of behaviour-preserving
renames, so **almost all of the verification belongs to seams that already exist.** No new test project, no new
fixture, and no new test-only abstraction is introduced.

### Seams used

**Seam one — `dotnet format --verify-no-changes`.** This is the highest available seam for the cleanup and it is a
single command whose exit code is the whole assertion. After the 29 fixes it must exit zero. It needs no test code
because the check *is* the test, and it is the same command the pipeline runs, so a green local run and a green CI run
are the same fact.

**Seam two — the existing test suites, unchanged.** All 29 fixes are behaviour-preserving: constant renames, one
suppression comment, one unused-parameter removal, one cancellation-token argument. The existing suites — 543 tests
across the unit, analyzer, integration and OData projects — are the safeguard that the renames changed nothing. **No
new tests are written for the cleanup**, and any need to modify an existing test's expectations would be a signal that
a rename went further than intended.

Prior art for how those suites are structured, when a test's expectations do need touching: `TestBase` in the
integration project for the transaction-per-test pattern, `SchemaFileParserTests` for pure unit coverage, and the
conformance fixtures in the OData project.

**Seam three — the pipeline itself, verified by running it.** There is no workflow-linting infrastructure in the
repository and none is added; a YAML linter would verify syntax, not that the jobs do the right thing. Verification is
therefore empirical, and the sequencing makes that cheap:

- Before pushing, the full local sequence must be green: `dotnet format --verify-no-changes` exiting zero, then
  `dotnet build`, then `dotnet test`, run strictly sequentially.
- The push that lands the cleanup touches `src`, so it fires the new pipeline end to end. Because `PgSqlVersion` is
  unchanged, `--skip-duplicate` makes the publish step a no-op. That makes the first real run a **live rehearsal**:
  every job executes, including the container-backed suites and the push, with no package actually released.
- `workflow_dispatch` remains available to re-run on demand if a container failure turns out to be transient.

### What is explicitly not tested

The container runtime's availability, image-pull reliability and runner capacity are properties of the GitHub-hosted
environment, not of this repository. If `postgres:18` fails to pull, the job fails loudly with the registry's own
error, which is the correct outcome — the fix belongs in the environment, not in a test.

## Out of Scope

- **Changing what gets published.** Same two packages, same feed, same duplicate-skipping semantics. Only the upload
  tool changes, and only because `ubuntu-latest` forces it — see the decision above.
- **Restructuring the test suites to avoid containers.** Testing against a real PostgreSQL is deliberate.
- **A pull-request build.** Work lands by direct push to `main`.
- **Dependency caching.** There are no `packages.lock.json` files, so `setup-dotnet`'s cache cannot be used without
  introducing them, and a hand-rolled cache would add a failure mode to the one path that must not be flaky. The
  pipeline now runs only on publish-worthy pushes, so restore cost is paid rarely.
- **Relaxing the naming conventions.** The code is brought to the documented rule, not the reverse. Any future argument
  that camelCase local constants should be legal is a separate change to `conventions.md` and `.editorconfig`.
- **Broader style enforcement beyond what the gate already covers.** The gate is the full `dotnet format` check; no new
  analyzers or severities are introduced alongside it.
- **A status badge in `README.md`.**
- **A version bump.** Nothing observable to a consumer changes.

## Further Notes

- Measured baseline, for judging whether the pipeline's cost is acceptable: `dotnet test -c Release --no-build` over
  the whole solution takes about **50 seconds** locally with warm images, running all four assemblies concurrently. The
  integration suite alone accounts for about 40 of those seconds; the OData suite about 3. An incremental Release build
  takes about 5 seconds and `dotnet format --verify-no-changes` about 12.
- Release build output for the whole solution is about **150 MB across roughly 1,100 files** — comfortably within
  artifact limits, which is what makes the build-once structure practical.
- The gating trade-off was considered and accepted: gating makes every publish slower and a flaky container can block a
  release. A blocked release is recoverable by re-running the job; a bad package on NuGet.org is not. No automatic
  retry is added, deliberately — a retry would mask exactly the container flakiness worth knowing about.
- Watch the first few runs for whether the build-output artifact round-trip actually beats recompiling in the `test`
  job. If the artifact plumbing proves slow or flaky, the fallback is for `test` to restore and build the solution
  itself; the `publish`-pushes-the-gated-package guarantee is independent of that choice and survives either way.
