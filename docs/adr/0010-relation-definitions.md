---
status: accepted
---

# Declare a relation as a class, and let it carry a condition

[ADR 0005](0005-table-relations-on-relation-properties.md) declared a **Relation** on an attribute: the property's type
named the target, the attribute's argument named the foreign key, and the far end was always the target's primary key.
[ADR 0006](0006-composite-primary-keys.md) admitted a composite key by making that argument variadic, paired
positionally against the target's key order. Both said everything a relation could say — which columns, and which
target — and both said it in two different places: the target in the property's type, the columns in strings on an
attribute resolved by the analyzer. There was nowhere to put anything else.

That left a common table shape undeclarable: a table holding a kind column beside an identifier, where the kind decides
which table the identifier belongs to. Rails calls this `belongs_to :polymorphic` and Hibernate calls it `@Any`; this
library had no equivalent, and neither has Entity Framework Core. Reaching those targets without it means adding a real
column per kind — a stored generated column holding the identifier only when the kind matches — and declaring an
ordinary relation against each one. One driving consumer with this shape has six link tables and three ordinary tables
carrying the pair, with a kind column selecting among roughly twenty-three tables: about ninety C# members, forty-five
foreign-key properties and forty-five relation properties, each mirrored onto a generated data type. The database
columns are not this library's fault and do not go away. The ninety C# members would, if a relation could say which
kind it reaches.

We decided that a **Relation** is declared by a class deriving from `RelationDefinition<TDeclaring, TTarget>`, naming
both **Table definitions** in its type arguments, stating the **Relation keys** that resolve it as compiler-checked
expressions, and stating an optional **Relation condition** — any expression over the two rows — that narrows it
further. This replaces the attribute-argument mechanism rather than sitting beside it: there is one way to declare a
relation now, one mechanism instead of two.

```csharp
[Table("public.generated_polymorphic_links")]
public partial class PolymorphicLinkTable
{
   [PrimaryKey] [Generated] public long LinkId { get; set; }
   public LinkTargetKind Kind { get; set; }
   public long TargetId { get; set; }

   private PersonRelation? Person { get; set; }

   private class PersonRelation : RelationDefinition<PolymorphicLinkTable, LinkPersonTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TargetId, y => y.PersonId),
      ];

      public override Expression<Func<PolymorphicLinkTable, LinkPersonTable, bool>> Condition
         => (link, person) => link.Kind == LinkTargetKind.Person;
   }
}
```

The relation property is `private` here alongside its `private` nested definition class — a public property could not
be typed as a less-accessible nested class (`CS0053`), so the two accessibilities travel together. Both classes are
purely declarative in the same sense a Table definition is: never instantiated, never executed. The generator reads
what the class says from source and inlines it into the association it already registers.

## Considered options

**Whether to add a discriminator concept or generalize the condition:**

- **A general `Relation condition` — any expression over the two rows (chosen).** A kind comparison is one instance of
  it, not a distinct mechanism. Generalizing costs nothing extra when the condition is omitted, and it composes with
  filtering and materializing alike, because it belongs to the correspondence rather than to any one query.
- **A dedicated discriminator attribute or property, naming a column and a value.** Rejected: it solves only the
  motivating case and still needs `nameof` strings the compiler cannot check for the value side, which is the same
  complaint this decision exists to fix for the column side.

**How much of the relation to fold into one mechanism:**

- **A class carrying both the keys and the condition (chosen).** Every relation now takes about five lines where a
  plain one took two under the attribute form — the cost is real and is accepted deliberately, in exchange for
  removing the ninety-member tax the problem statement describes and for closing the door on a second mechanism
  existing beside the first.
- **Keep the attribute for a plain relation and add a class only for a conditioned one.** Rejected: two mechanisms for
  one concept, which is the complaint against the pre-existing attribute-plus-nothing-else design repeated rather than
  resolved. A reader would have to learn which shape a given relation used before knowing where to look for what it
  says.
- **A class-level attribute naming both tables and the pairs via `nameof`.** Rejected for the same reason ADR 0005
  rejected it for foreign keys alone: it needs strings for something the type system can carry, and a rename cannot
  break what a string does not track.

**How the column pairs are stated:**

- **A pair of expressions per column, via a `protected static Key(…)` helper (chosen).** A pair whose two sides hold
  different types does not compile — the type check that used to strip nullability from the old form's positional
  match now happens once, in `Key`'s own signature, via two overloads: matching types, and a nullable left side against
  a non-nullable right. There is no third overload for a nullable right side; see Consequences.
- **Positional matching against the target's key, as ADR 0005 and ADR 0006 did.** Superseded by this decision. It
  works only because the far end of every relation was always the target's primary key — the moment a relation may
  pair against a `[Unique]` column instead, position alone cannot say which member of the key each name is paired
  against, and **Key order** stops being enough. Stating each pair as two expressions removes the ordering rule
  entirely: `Keys` is a set, joined with `&&`, and reordering its entries changes nothing.

**Whether pairs and target uniqueness are checked once or duplicated per declaration form:**

- **One resolver path over resolved key pairs, regardless of which form declared them (chosen).** The uniqueness
  claim, the tenancy pairing check and the forgotten-condition check all read `ResolvedRelation`'s pairs rather than
  anything about declaration syntax, so they apply identically to old-form and new-form relations while both existed
  during the migration, and unconditionally now that only one form remains.
- **Duplicate the checks per form.** Rejected: doubles the surface for the same three rules to drift apart on.

## Consequences

- **This supersedes the declaration half of ADR 0005 and absorbs ADR 0006's composite-key story.** A relation's target,
  cardinality and column pairs are now stated by a `RelationDefinition<,>` class rather than by a property's type plus
  an attribute argument; ADR 0005's and ADR 0006's decisions on *that* question no longer describe what ships. Both
  ADRs stay in place, pointing here, the same pattern ADR 0005 used on ADR 0004: the record that single-table querying,
  and then attribute-driven relations, were each reasoned in their turn is worth keeping even after superseded.
  Everything else those two ADRs decided is untouched by this one: relations still register through the provider's
  predicate association overload (ADR 0006), still resolve one-directional and unpaired (ADR 0005), still fold a
  to-one relation into a single outer join and cost a to-many relation one statement per level (ADR 0005), still
  report an invalid relation without abandoning the table (ADR 0005), and the composite-key consequences about
  nullable key members and index shape (ADR 0006) are unaffected — key pairs still resolve to the same join shape,
  only the syntax that states them changed.
- **`RelationAttribute` becomes a bare marker.** Its constructor and its foreign-key-name argument are gone; writing
  `[Relation]` at all is now optional, accepted only on a property already typed as a relation definition, and fails
  the build on any other property so the attribute can never say something untrue. Every existing
  `[Relation(nameof(...))]` declaration stops compiling (`CS1729`, no matching constructor) rather than continuing to
  work under different rules — deliberately, per the spec's requirement that an upgrade never leave a developer
  half-migrated without knowing it.
- **Nothing changes about what a relation still does not do.** It creates no schema and verifies nothing against the
  real database. A developer who wants the database to refuse a link pointing at a row that does not exist still
  writes the generated column and its foreign key in a migration by hand, exactly as before this decision.
- **The nullable-target-side question is settled by refusal, not by a third overload.** `Key(…)` needs its
  nullable-left overload because a foreign key may hold null while the primary key it targets never can — that
  assumption holds for every relation possible under the old form, because a primary key is never nullable. It stops
  holding once a relation may pair against a `[Unique]` column, because nothing in this library refuses a nullable
  `[Unique]` column outright. Rather than add a third `Key(…)` overload for a nullable target side, the build refuses
  the pairing (`PGSQL0035`): a nullable unique column matches at most one row but may match none for reasons the
  relation cannot see, so the refusal is right on its own merits and keeps `Key(…)` at exactly two overloads.
- **The condition's constant reaches PostgreSQL as a literal, except against a column carrying a value conversion.**
  The provider renders a constant in an association predicate as a literal on its own: a string, a number and an
  unconverted enum all appear inline in the join with no help from the generator. The exception is a column mapped
  with a value conversion, which is how this library maps every enum by default — text unless a `[Column]` claims an
  integral type. There the comparison binds the *converted* value as a parameter instead, and no wrapper changes
  that. Nothing in the emitted condition tries to force the issue, because nothing can:
  - `Sql.Constant(...)` — which the design originally called for and which earlier steps of this change emitted — is
    inert here. It makes no difference to any constant in an association predicate, converted or not; the ones that
    already render inline still do, and the converted one still binds.
  - `Sql.ToSql(...)` and `Sql.AsSql(...)` do force a literal past the conversion, and are wrong for exactly that
    reason: they emit the enum's underlying number, so a kind column stored as text is compared against `1` rather
    than `'Person'`. That is silently wrong SQL against a text column, and a type error in PostgreSQL. A literal is
    not worth buying at the price of the wrong literal.

  So the emitted condition wraps nothing, and the shortfall is confined to a converted column. It costs no
  correctness anywhere: the parameter carries the converted value, so every relation still narrows to the right kind
  in both directions, several conditioned relations sharing a pair still resolve independently, and a relation to one
  row still folds into a single left join with no `IS NULL` widening. What it costs is the per-kind query plan the
  spec asked for, and only for a conditioned relation whose condition compares a converted column. In practice two
  such relations already differ in SQL text because they reach different target tables. Claiming an integral storage
  for the kind column does not recover it — a cast conversion binds a parameter just as a text one does — so the
  honest summary is that the spec's story 33 holds for an unconverted column and not for a converted one, and this is
  recorded here rather than promised in the README.
- **The uniqueness claim, the tenancy check and the forgotten-condition check are reshaped to read resolved pairs,
  which changes what two of them report.** The tenancy check (`PGSQL0027`, same id) becomes pair-based and
  direction-free: a tenancy column on either table's side of a pair must be paired with a tenancy column on the other
  side, and a tenancy column absent from every pair warns too. This is stronger than the positional rule it replaces,
  which only ever checked whichever side held the primary key, and it now covers the declaring side as well. It fires
  only when *both* tables are tenanted, which is the one narrowing taken against the spec's literal wording. A
  relation reaching a wholly untenanted table cannot reach another tenant's rows, because the rows on the far side
  belong to no tenant: a tenanted table reading a shared lookup is a common shape, it carries none of the risk the
  warning exists for, and the developer's only response to a warning there would be to silence it. Pairing also needs
  a tenancy column on both sides in order to pair at all, so where one side has none the warning could not say what
  to do. Stories 37 to 40 all describe relations between two tenanted tables and are unaffected. The
  uniqueness warning (`PGSQL0031`) is new rather than reshaped: it replaces the arity check ADR 0006 introduced
  (`PGSQL0019`), which had nothing left to check once a relation states its pairs explicitly instead of matching a
  count against the target's key — what that arity check protected, a relation to one row reaching more than one, is
  now a claim about target-side uniqueness instead of a count.
- **Every relation now costs about five lines where a plain one cost two.** This is the price of one mechanism instead
  of two, and it is accepted deliberately rather than incidentally: the alternative was carrying both the attribute
  form and the class form indefinitely, which is the complaint this decision exists to resolve, restated.
- **API surface.** `RelationAttribute` loses its constructor and its property — binary- and source-breaking for every
  existing declaration. `RelationDefinition<TDeclaring, TTarget>` and `RelationKey` are new public types. The two
  key-expression overloads on `QueryEntityMappingBuilder<TEntity>` that a single-column relation used are removed, since
  every relation now registers through the predicate overload ADR 0006 introduced. Diagnostics: `PGSQL0012`,
  `PGSQL0013` and `PGSQL0019` are retired and their ids are never reused; `PGSQL0028` through `PGSQL0035` are new;
  `PGSQL0014`, `PGSQL0015` and `PGSQL0027` are reused, reading a definition-form relation's resolved pairs where they
  used to read the attribute form's. This breaks a published package. Pre-1.0 that is a MINOR bump: `0.36.0` to
  `0.37.0`.
