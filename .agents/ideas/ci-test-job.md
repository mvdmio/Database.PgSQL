# Run the tests in CI

Status: ready-for-agent — specced in `.agents/specs/ci-test-job.md`, which resolves every open question below.

## Motivation

The pipeline publishes packages but never runs a test. It triggers only on changes to the library sources, builds, packs, and pushes to the package feed. There is no pull-request build, no test run, and no formatting check. The reference documentation states the position explicitly and asks contributors to run the tests locally before merging.

That works while the only contributor remembers to. It stops working the moment someone does not — a published package is not something you can take back, and the first signal that a change broke something would be a consumer reporting it.

The gap widened with the query surface, whose behaviour is only observable against a real database, and widens again with the OData conformance suite, whose entire purpose is to catch regressions that no amount of reading the code would reveal. Both are integration suites requiring containers; neither runs anywhere except on a developer's machine.

## Goal

Have the test suites run automatically on changes, so that a regression is caught before a package reaches the feed rather than after.

## Decisions (locked)

None.

- One constraint: the integration suites need a container runtime, and each test assembly starts its own PostgreSQL container. Any design has to account for more than one running concurrently.

## Out of scope

- Changing what the publish step does once tests pass. Packaging and pushing already work.
- Restructuring the test suites to avoid containers. Testing against a real PostgreSQL is deliberate.

## Open questions

- Does the test run gate publishing, or run alongside it? Gating is the point, but it makes every publish slower and a flaky container failure would block a release.
- Should there be a pull-request build at all, given the current workflow is direct pushes to the main branch?
- The publish trigger is scoped to library source changes, so a change to a test project triggers nothing today. Should tests run on changes to anything?
- Is a formatting check worth adding at the same time, given the formatter and the repository disagree about line endings?
- Which runner and container setup, and does the pinned database image pull reliably in that environment?
- How long does the full suite take with the containers, and is that acceptable in the path to a release?
