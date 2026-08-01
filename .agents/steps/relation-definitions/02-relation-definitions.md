# 02 — Declare a relation as a class

Status: pending

## What to build

A developer declares a **Relation** by writing a **Relation definition**: a class deriving from
`RelationDefinition<TDeclaring, TTarget>` that names both **Table definitions** in its type arguments and states the
**Relation keys** as pairs of property expressions. The **Relation property** is typed as that class, or as a
supported collection of it, and that type alone is what makes it a relation — no attribute is needed and the
cardinality is read the way it always was.

```csharp
[Table("books")]
public partial class BookTable
{
   [PrimaryKey] public long BookId { get; set; }
   public long? AuthorId { get; set; }

   public AuthorRelation? Author { get; set; }

   private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}
```

The class may be nested inside the Table definition it belongs to — private is fine, because nothing instantiates it
and nothing calls it — or declared anywhere else, because the type arguments say which tables are involved wherever it
lives. `Key(…)` is generic over the column's type, so a pair whose two sides hold different types does not compile,
and it has exactly two overloads: matching types, and a nullable left side against a non-nullable right, which is the
ordinary outer-join case. There is deliberately no third overload for a nullable right side; step 04 refuses that
shape outright.

The order the pairs are written in carries no meaning — they are joined with `&&` — and neither side has to be the
one that "owns" the foreign key, so both directions of a relation read alike. A relation stays one-directional:
declaring one never creates the other.

The old attribute-argument form keeps working unchanged. That is what keeps the tree green while the rest of the
branch lands: this step adds a second, type-driven path beside the attribute-driven one and removes nothing. Step 06
takes the old one away.

### What the generator does

- The relation-property split becomes type-driven. A property whose type derives from `RelationDefinition<,>`, or is
  one of the already-supported collection types over such a type, is a relation property. `HashSet<T>` stays
  unsupported, as it is today.
- The target and the declaring table come from the type arguments rather than from the property's type and its
  enclosing class. The target must be a `[Table]` class in the same compilation.
- Key pairs are read from the `Keys` override's syntax. Each side must be a direct property reference resolving to a
  mapped column of its own table.
- Emission is the predicate association step 01 settled on: one equality per pair, combined with `&&`, always an outer
  join.
- The generated data type is unchanged in shape: the relation still appears there as the target's generated data type,
  or a list of it — never as the relation definition class.

### Diagnostics this step owns

New ids, continuing from `PGSQL0028` in the order the spec fixes. Later steps take the ones not listed here, so do not
renumber.

| Id | Rule | Severity | Trigger |
| --- | --- | --- | --- |
| `PGSQL0028` | Relation declaring table mismatch | Error | The `TDeclaring` type argument is not the Table definition the relation property is declared on |
| `PGSQL0029` | Relation states no keys | Error | The `Keys` override yields no pairs |
| `PGSQL0030` | Relation key is not a column reference | Error | Either side of a pair is not a direct reference to a mapped column of that table |

Reshaped: `PGSQL0014` (relation target is not a Table definition) reads the target from the type argument, because no
generic constraint can express "is a `[Table]` class in this compilation". `PGSQL0015` (relation to one row must be
nullable) keeps applying to the relation property. `PGSQL0016`, `PGSQL0017` and `PGSQL0018` keep their meanings for a
definition-typed property.

Blast radius is unchanged from ADR 0005: a relation-level error drops that relation and nothing else, so the table
still generates and the developer reads one message rather than a wall of type-not-found errors.

### Proving it end to end

The generator harness is the seam for everything on the declaration side, and its runtime stubs must gain
`RelationDefinition<,>`, `RelationKey` and `Key(…)` so a test never passes on a shape that would not compile for a
real consumer. Keep the "emitted source compiles" assertion on every new test class — that is the guard.

Then convert the author-and-book fixtures in the integration suite — the author, book, book-tag and tag tables, nine
declarations between them — to the new form, and leave their tests alone. Those declarations are already exercised
against a real container, so passing them unchanged is what shows the new form works end to end rather than only in
the harness. They cover a relation to one row, a relation to many, a table relating to itself in both directions, and
two relations at the same target.

### Boundaries

- The tenant tables in the integration suite, the OData fixtures and the analyzer test sources stay on the old form;
  steps 04, 05 and 06 move them.
- Add the three new rows to `AnalyzerReleases.Unshipped.md` with their titles verbatim. Leave `README.md`, the
  library's `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns them.

## Acceptance criteria

- [ ] `RelationDefinition<TDeclaring, TTarget>` ships in the library: abstract `Keys` returning
      `IReadOnlyList<RelationKey>`, `protected static Key(…)` with exactly two overloads, `RelationKey` an opaque value
      nothing reads at run time. Public surface carries `[PublicAPI]` and XML docs.
- [ ] A property typed as a relation definition is a relation to one row; a supported collection of one is a relation
      to many; neither needs `[Relation]`, and writing `[Relation]` on one is still accepted.
- [ ] A relation definition nested privately inside its Table definition and one declared outside it both resolve.
- [ ] The pairs' written order does not change the emitted join, and a pair stating the target side first resolves the
      same as one stating the declaring side first.
- [ ] Mismatched pair types fail to compile rather than reaching a diagnostic; a nullable left side against a
      non-nullable right compiles.
- [ ] `PGSQL0028`, `PGSQL0029` and `PGSQL0030` each fire on their trigger, drop only the relation they describe, and
      leave the rest of the table generating.
- [ ] `PGSQL0014` reports a target read from `TTarget`, and `PGSQL0015` still reports a non-nullable relation to one
      row.
- [ ] The harness stubs carry the new runtime types, and every new generator test class asserts that the emitted
      source compiles.
- [ ] The author, book, book-tag and tag fixtures in the integration suite are declared in the new form and their
      existing tests pass unchanged.
- [ ] Every relation still declared in the old attribute-argument form still resolves and still works.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).
