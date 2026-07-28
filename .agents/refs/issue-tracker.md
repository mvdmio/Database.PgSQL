# Issue tracker: Local Markdown

Issues, specs and ideas for this repo live as markdown files in `.agents/`.

## Conventions

- Specs, plans and PRDs are `.agents/specs/<slug>.md` — one file per feature
- Ideas awaiting a grilling session are `.agents/ideas/<slug>.md`
- Reference docs that support the skills are `.agents/refs/<slug>.md`
- ADRs are `docs/adr/<NNNN>-<slug>.md`
- Triage state is recorded as a `Status:` line near the top of each spec or idea file (see `triage-labels.md` for the role strings)
- Comments and conversation history append to the bottom of the file under a `## Comments` heading

## When a skill says "publish to the issue tracker"

Create a new file at `.agents/specs/<slug>.md` (or `.agents/ideas/<slug>.md` for an idea that has not been specced yet).

## When a skill says "fetch the relevant ticket"

Read the file at the referenced path. The user will normally pass the path or the slug directly.
