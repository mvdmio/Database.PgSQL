# OData over the query surface

This project is two things at once: the conformance suite that establishes which OData query options the
`mvdmio.Database.PgSQL` query surface answers correctly, and the worked example of wiring an OData endpoint onto a
generated repository's `Query()`.

There is no integration package, no documentation page and no maintained sample for this combination of query provider
and OData. Everything below was established by running it against a real PostgreSQL database rather than read out of
documentation, and the results tables are pinned by tests here. Two things are documented but *not* tested, and both say
so where they appear: the hosted failure mode, and how many statements a to-many `$expand` issues.

## Read this first: two settings you must set

OData decides how defensively to rewrite your `$filter` by matching the query provider's namespace against a hardcoded
allowlist of Microsoft providers. This library's provider is not on that list and cannot join it, so OData assumes the
worst.

**1. Disable null-propagation handling.** This is not a tuning choice:

```csharp
var settings = new ODataQuerySettings {
   HandleNullPropagation = HandleNullPropagationOption.False
};
```

Left at its default, OData guards every property access with a null check. The consequences:

| Symptom | Effect |
|---------|--------|
| `substring` does not translate at all | the request fails |
| every predicate is wrapped in `CASE WHEN col IS NULL` | correct rows, but PostgreSQL cannot use an index |
| collection `all()` returns the wrong rows | silently wrong results |
| `$expand` over a relation to many rows returns an empty collection | silently missing results |

The rows still come back right for most filters, which is exactly what makes it dangerous — nothing about the response
tells you the endpoint is misconfigured. The last two are the worst of them and are spelled out under
[expansion](#the-two-symptoms-that-need-a-relation-to-see).

**2. Enable the query options you want, and validate.** Every option is off by default and `$top` is capped at zero, so
an unconfigured endpoint rejects every query string:

```csharp
services.AddControllers().AddOData(
   options => options
      .Select()
      .Filter()
      .OrderBy()
      .Count()
      .Expand()
      .SetMaxTop(100)
      .AddRouteComponents("odata", GetEdmModel())
);
```

Also narrow the allowed-function set, so a function that has no SQL translation comes back as a client error rather than
a server error:

```csharp
private const AllowedFunctions SupportedFunctions =
   AllowedFunctions.AllFunctions
   & ~AllowedFunctions.MatchesPattern
   & ~AllowedFunctions.IsOf;

private static readonly ODataValidationSettings ValidationSettings = new() {
   AllowedFunctions = SupportedFunctions,
   MaxExpansionDepth = 2
};
```

The expansion depth cap is not tuning either, and [expansion](#expansion) says why.

## Wiring it up

```csharp
[Table("public.users")]
public partial class UserTable
{
   [PrimaryKey]
   [Generated]
   public long UserId { get; set; }

   public string UserName { get; set; } = string.Empty;
   public DateTimeOffset? LastLoginAt { get; set; }
}
```

Build the model from the *generated data type*, not the table definition — `UserData` is what `Query()` returns:

```csharp
private static IEdmModel GetEdmModel()
{
   var builder = new ODataConventionModelBuilder { Namespace = "MyApi" };
   builder.EntitySet<UserData>("Users").EntityType.HasKey(x => x.UserId);

   return builder.GetEdmModel();
}
```

Then hand `Query()` to the endpoint. Apply the options yourself and **materialize inside the action** — returning the
`IQueryable` and letting `[EnableQuery]` do the work is shorter, and it is a trap; see
[the hosted failure mode](#the-hosted-failure-mode) below.

```csharp
public class UsersController : ODataController
{
   private static readonly ODataQuerySettings QuerySettings = new() {
      HandleNullPropagation = HandleNullPropagationOption.False
   };

   private readonly IUserRepository _users;

   public UsersController(IUserRepository users)
   {
      _users = users;
   }

   [HttpGet]
   public IActionResult Get(ODataQueryOptions<UserData> options)
   {
      try
      {
         // Without [EnableQuery] this is yours to call. Skipping it still gives a working endpoint — with a worse error
         // contract, because a blocked function then fails during translation instead of during validation.
         options.Validate(ValidationSettings);

         var query = options.ApplyTo(_users.Query(), QuerySettings);

         // Enumerated here rather than in the output formatter. Enumerable.Cast, not Queryable.Cast: the element type
         // after $select or $apply is one of OData's wrappers, so there is nothing to name statically.
         return Ok(((IEnumerable)query).Cast<object>().ToList());
      }
      catch (ODataException exception)
      {
         return BadRequest(exception.Message);
      }
      catch (QueryTranslationException exception)
      {
         return BadRequest(exception.Message);
      }
   }
}
```

If you do not allow `$select` and `$apply`, the element type never changes and you can keep it typed and asynchronous:

```csharp
var query = (IQueryable<UserData>)options.ApplyTo(_users.Query(), QuerySettings);

return Ok(await query.ToListAsync(ct));
```

This suite hosts nothing, so treat the controller above as the shape to aim for rather than a tested artifact. The
settings it passes and the behaviour of the query it composes are the tested part.

## Conformance results

### Query options

| Option | Result | Reaches the database as |
|--------|--------|-------------------------|
| `$filter` | works | `WHERE`, with runtime values as parameters |
| `$orderby` | works | `ORDER BY`, including compound and descending |
| `$top` / `$skip` | works | `LIMIT` / `OFFSET` |
| `$count` | works | a separate `SELECT COUNT(*)` that selects no column |
| `$select` | works | a narrowed column list, plus the key |
| `$apply` | works | `GROUP BY` with SQL aggregates; `filter(…)/groupby(…)` becomes one statement |
| `$expand` | works | to one row, an outer join in the same statement; to many rows, statements beyond it — see [expansion](#expansion) |
| `$skiptoken` | works | a lexicographic ladder over the ordering properties in the `WHERE` — see [composite keys](#composite-primary-keys) |
| `$search`, `$compute`, `$batch` | untested | — |

`$select` deserves a note: it works and it genuinely narrows the columns queried, despite OData projecting into wrapper
types of its own and despite bug reports to the contrary. `$apply` is the best-supported area of the whole surface.

### Composite primary keys

A table definition may declare a composite primary key, so the front-end has to cope with one. `$filter`, `$orderby`,
`$top`, `$skip`, `$count`, `$select`, `$skiptoken`, navigation-path filtering in both cardinalities and `$expand` — the
options where key arity could plausibly matter — are each pinned over an entity whose key is **two** columns as well as
over the single-column entities. `$apply` and the `$filter` function families are pinned over single-column keys only:
neither touches a key.

Nothing in OData's query-option application requires a single-property key, and the model builder takes a composite key
either as chained per-property `HasKey` calls or as one anonymous-type selector — this suite uses the chained style, the
same one the single-key fixtures use.

Two things behave differently enough to state:

| Construct | Over a composite key |
|-----------|----------------------|
| `$select` | Appends **every** key column, not one, so a narrowed projection is as many columns wider than you asked for as the key has members |
| `$skiptoken` | The token names one value per ordering property, and becomes a lexicographic ladder: strictly greater on the first member, or equal on it and greater on the next |
| `$filter` / `$orderby` through a navigation property | The join carries every key column, so the relation cannot reach a row outside the tenant the leading key member names |
| `$expand`, both cardinalities and nested | Unchanged. The association the generator registers is a join predicate rather than a pair of key expressions, and the front-end never sees the difference |

Declare the key explicitly here as well — convention-based discovery finds neither member. `$skiptoken` is off by default
like every other option; turn it on with `options.SkipToken()` in `AddOData`, or by setting `EnableSkipToken` on the query
configuration when you drive the options yourself.

### Reaching through a relation

A table definition that declares a relation gets a navigation property on its generated data type, and a query string can
reach through it without expanding it:

| Construct | Result | Reaches the database as |
|-----------|--------|-------------------------|
| `$filter=Author/Name eq 'tolkien'` | works | a `LEFT JOIN` and a parameterized `WHERE` on the joined table |
| `$orderby=Author/Name desc` | works | a `LEFT JOIN` and an `ORDER BY` on the joined column |
| `$filter=Books/any(b: b/Title eq 'narnia')` | works | a correlated `EXISTS` subquery |
| `$filter=Books/all(b: b/Title eq 'hobbit')` | works | a correlated `NOT EXISTS` subquery |

A relation is an outer join, so a row whose foreign key points nowhere is still returned by a query that only sorts by the
far side. And `all()` over an **empty** collection is true, as OData Part 2 specifies — an author with no books satisfies
every `all()` predicate. An endpoint replacing one backed by a provider that drops those rows will see the difference.

`all()` is also the second symptom of the misconfiguration above, and the one worth knowing about: see
[expansion](#expansion).

### Expansion

`$expand` works. It does **not** go through the library's `Include` and `ThenInclude` operators — OData binds an
expansion as a projection into wrapper types of its own and selects the navigation property inside it, so none of that
machinery is on this path. What makes the projected member translatable is the provider-level association the generator
registers for each relation.

What it costs depends only on the cardinality:

| Relation | Reaches the database as |
|----------|-------------------------|
| To one row | An outer join in the query's own statement, related columns included. No extra round trip |
| To many rows | Nothing in the query's own statement — the related rows arrive, so at least one further statement runs |

**How many further statements is not stated here, because this suite cannot count them.** It can see the SQL a composed
query renders to and the last statement sent through the connection, and the detail statement is neither: the provider
runs it ahead of the query that derives its parents, so the last statement after materializing an expansion is the main
query. That is pinned too, so the gap is recorded rather than assumed. Treat a to-many expansion as at least one extra
round trip per level and measure if it matters.

Every nested option is individually supported, so you can enable them one at a time:

| Nested option | Result | Notes |
|---------------|--------|-------|
| `$expand=Books($filter=…)` | works | narrows the related rows themselves; every parent is still returned |
| `$expand=Books($select=…)` | works | narrows to exactly the named properties — unlike a top-level `$select`, the key is not added back |
| `$expand=Books($orderby=…;$top=n)` | works | applied per parent, not across the whole detail set |
| `$expand=Books($count=true)` | works | a correlated `COUNT(*)` in the query's own statement, so the count costs no round trip even though the rows do |
| `$expand=Books($expand=Author)` | works | two levels; this is the deepest thing the suite covers |
| `$expand=Mentor($levels=2)` | works | two joins against the same table, and it stops at two — the chain continues in the data |
| `$expand=*` | works | every navigation property, one level deep |

Ordinary data does not break it. An expansion across a foreign key that is null yields an **absent** navigation property
rather than an error, and expanding a relation with no matching rows yields an **empty collection** — which is also what
"not asked for" looks like, so a client tells the two apart by whether it sent `$expand`, not by what came back.

Enable it and cap it:

```csharp
services.AddControllers().AddOData(options => options.Select().Filter().OrderBy().Count().Expand()…);

private static readonly ODataValidationSettings ValidationSettings = new() {
   AllowedFunctions = SupportedFunctions,

   // Relations are declared one direction at a time and are never paired, so a model that declares both directions
   // contains a cycle by construction — see ADR 0005. This is the only thing bounding a client walking around one.
   MaxExpansionDepth = 2
};
```

A request deeper than the cap comes back as an `ODataException` from **validation**, before anything reaches the
database — the same error contract this suite draws for a blocked `$filter` function, and a client error rather than a
server one. `$levels` counts against the same cap, which is what matters: a self-reference is the cheapest way to walk a
cycle.

Two things about the EDM model are worth knowing before you build one from generated data types:

- **Declare the key explicitly.** Convention-based key discovery looks for `Id` or `<TypeName>Id`, and a table
  definition's key — `AuthorId` on `AuthorData` — is neither, so leaving it to convention makes model building fail.
- **The cycle is expected.** Two relations in opposite directions produce one, and a self-referencing relation produces
  one on its own. There is nothing to remove; the depth cap is what bounds it.

Giving an expandable type its own entity set is not required to expand it — the expansion is a projection — but a
consumer that exposes such a type routes to it anyway, so the model and the routes match.

#### The two symptoms that need a relation to see

Both belong to the null-propagation misconfiguration at the top of this document, and both were asserted in prose long
before anything tested them. They hold:

| Symptom | With the setting disabled | Left at its default |
|---------|---------------------------|---------------------|
| `$expand` over a relation to many rows | the related rows | an **empty collection** for every parent, no error |
| `$expand` over a relation to one row | the related row | the related row — unaffected |
| `Books/all(b: …)` | the rows OData specifies, including parents with an empty collection | **different, wrong rows**: OData adds an `EXISTS` on top of its own `NOT EXISTS`, so a parent with an empty collection no longer qualifies |

The expansion symptom is the worst thing on this page, and the reason is in the first column of the table rather than the
second: **the query surface composes and sends exactly the same statement either way**. There is no failure to catch, no
diagnostic to read, and an empty collection is what an author with no books legitimately looks like. If you are auditing
an endpoint you already shipped, check the to-many expansions and nothing else — the to-one direction folds into the main
statement as a join and survives the rewriting intact.

Tell this apart from a genuine translation refusal by what you get: a refusal is a `QueryTranslationException` naming the
construct that could not be translated, and this is a `200` with plausible-looking data in it.

### `$filter` functions

Every function below translates to SQL and returns the right rows.

| Family | Functions |
|--------|-----------|
| String | `contains`, `startswith`, `endswith`, `indexof`, `length`, `substring` (both arities), `tolower`, `toupper`, `trim`, `concat` |
| Date and time | `year`, `month`, `day`, `hour`, `minute`, `second`, `date`, `time`, `fractionalseconds`, `now` |
| Arithmetic | `round`, `floor`, `ceiling`, `add`, `sub`, `mul`, `div`, `mod` |
| Cast | `cast` to `Edm.String` and `Edm.Decimal` |
| Membership and values | `in`, enum equality, `Edm.Guid` equality, a bare boolean property |

These do not:

| Function | What happens | Who is at fault |
|----------|--------------|-----------------|
| `matchespattern` | the provider refuses to translate `Regex.IsMatch` | the provider; its maintainers have declined to implement it |
| `isof` | the provider refuses to translate the type check | the provider |
| `mindatetime`, `maxdatetime`, `totaloffsetminutes` | `NotImplementedException: Unknown function` | OData; its own expression binder gives up before the provider is reached |

Exclude `matchespattern` and `isof` through `AllowedFunctions` and a client gets a validation error instead of a server
error. The other three cannot be excluded that way: this version of OData has no `AllowedFunctions` member for them, so
validation lets them through and then fails. If your clients might send them, reject them yourself.

### Null comparison differs from Entity Framework Core

`$filter=Nickname ne 'bobby'` returns the rows where `nickname` **is null**, as well as the rows where it holds
something else. An Entity Framework Core-backed endpoint drops the null rows.

This library's behaviour is the specified one — OData Part 2 §5.1.1.1 states that the null value is not equal to any
value but itself — and it matches the C# you would have written. If you are replacing an EF Core-backed endpoint behind
an existing API, this is a behavioural change your clients can see.

The cost is visible in the generated SQL, which widens every inequality with `OR col IS NULL`. That predicate cannot be
served from an index. Resist the temptation to "clean it up" by switching the provider to SQL-like comparison: it would
silently drop rows.

### Generated property types in an OData model

A table definition can carry property types with no EDM equivalent. Model building never fails on any of them, so
nothing warns you at startup — check this table before exposing a generated type:

| Property type | Becomes | Usable? |
|---------------|---------|---------|
| `bool`, `int`, `long`, `decimal`, `string`, `Guid`, `DateTimeOffset`, an enum | the matching EDM primitive or enum | yes |
| `DateOnly`, `TimeOnly`, `TimeSpan`, `byte[]`, `sbyte` | `Edm.Date`, `Edm.TimeOfDay`, `Edm.Duration`, `Edm.Binary`, `Edm.SByte` | yes |
| `char` | `Edm.String` | yes, widened to a one-character string |
| `ushort`, `uint` | `Edm.Int32`, `Edm.Int64` | in the model, yes, widened — but see the note below |
| `ulong` | `Edm.Int64` | **lossy** — values above `long.MaxValue` cannot be represented |
| `DateTime` | `Edm.DateTimeOffset` | by convention, not equivalence: the instant acquires an offset |
| `Uri` | a complex type with one collection of path segments | no — not comparable or filterable as a value |
| `Dictionary<string, string>` | a collection of a complex type with no properties | no — carries nothing |

Keep the `Uri` and `Dictionary<string, string>` properties out of your EDM model —
`builder.EntityType<UserData>().Ignore(x => x.HomePage)` — or expose a `string` property of your own alongside them.

Separately, and below OData rather than because of it: `sbyte`, `ushort`, `uint` and `ulong` properties cannot be
**written** through a generated repository at all. The PostgreSQL driver has no mapping for their `DbType`s and refuses
the parameter, so every insert and update on such a table throws. Reading and filtering work, which is why they appear as
usable in the table above. Avoid these four in a table definition until that is fixed.

## The hosted failure mode

The query surface raises a precise exception when it cannot translate an expression. **At a hosted endpoint that returns
an `IQueryable` directly, that exception is worthless.**

`[EnableQuery]` composes the expression tree but never enumerates it. Materialization happens later, inside the output
formatter, after the response headers have already been written — and the formatter has no exception handling. A
provider failure at that point produces a success status with a partial body and an aborted stream, not an error status.

The mitigation is in the wiring example above: apply the options yourself, materialize inside the action, and map
failures to a status code. Do that and a client gets a `400`; skip it and a client gets a truncated `200`.

## Running the suite

Docker must be running — the suite starts its own PostgreSQL container.

```bash
dotnet test test/mvdmio.Database.PgSQL.Tests.Integration.OData/mvdmio.Database.PgSQL.Tests.Integration.OData.csproj
```

## How the suite is built

- **No web host.** `[EnableQuery]` is an action filter and needs one; expression translation, which is what is under
  test, does not. `Fixture/ODataQuery.cs` parses a query string into `ODataQueryOptions<T>` over a stand-alone request
  and applies it to the queryable — the same thing the attribute does, minus HTTP.
- **The recommended configuration lives in one place**, `Fixture/ODataConfiguration.cs`. That is what the settings
  sections above document, and changing it changes the suite.
- **Tests assert rows and SQL.** Column narrowing, `LIMIT`/`OFFSET`, an aggregate count and parameterization are
  indistinguishable from a correct row set otherwise. Assertions target the shape that matters — a `GROUP BY` is
  present, a column list is narrowed — rather than whole SQL strings.
- **Six fixture entities, in four EDM models.** `SampleTable` carries only EDM-friendly types, so its model must build
  cleanly. `AwkwardTable` carries the awkward ones, kept separate so a model-building failure would break one test rather
  than the suite. `AuthorTable` and `BookTable` declare relations to each other and to themselves, and get a model of
  their own: adding a relation to `SampleTable` would put a navigable member in the conformance model, where the
  convention builder would discover it and change what every `$select` and `$apply` result already pinned there sees.
  `TenantProjectTable` and `TenantTaskTable` are the same shape again with two-column keys and a composite relation, in a
  fourth model for the same reason — the single-key pair keeps pinning the single-key path. Their shapes deliberately
  mirror the main integration suite's tables, so the two suites can be read against each other.
- **Every test rolls back.** A transaction opens before each test and rolls back after, so the suite runs repeatedly and
  in any order.
- **A second container.** This assembly is isolated from the main integration suite so the OData dependency stays out
  of everything the project ships.

## What is deliberately not covered

- **How many statements a to-many expansion issues** — not for want of trying. The provider runs the detail query ahead
  of the query that derives its parents, so the last statement sent after materializing an expansion is the main query
  and the detail statement has already been and gone. `Expand_ToManyRows_LeavesTheDetailStatementUnobservable` pins that,
  so the gap is recorded rather than assumed. Making it visible would take a diagnostics facility, which is deliberately
  not being added.
- Nested `$compute` and `$search`, a raw `$count` on a navigation path, and selecting a navigation property without
  expanding it.
- A many-to-many fixture shape. A join table would exercise a shape, but no query-string construct that two-level nested
  expansion does not already reach.
- `$search`, `$compute`, `$batch`.
- **Generating** a `$skiptoken`. Applying one is covered, over a composite key as well as a single one; producing the
  next-page link is the serializer's job and needs a host.
- Hosting. No controllers, no minimal-API endpoints, no `WebApplicationFactory`. The hosted failure mode above is
  documented, not tested — the in-process suite materializes the queryable itself and so sees a clean exception.
- Frameworks other than `net10.0`. The library's framework coverage is proven by its own build.
