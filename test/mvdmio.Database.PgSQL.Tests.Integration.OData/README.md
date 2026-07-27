# OData over the query surface

This project is two things at once: the conformance suite that establishes which OData query options the
`mvdmio.Database.PgSQL` query surface answers correctly, and the worked example of wiring an OData endpoint onto a
generated repository's `Query()`.

There is no integration package, no documentation page and no maintained sample for this combination of query provider
and OData. Everything below was established by running it against a real PostgreSQL database rather than read out of
documentation, and the results tables are pinned by tests here. Two things are documented but *not* tested, and both say
so where they appear: the hosted failure mode, and the collection-quantifier symptom of the misconfiguration below.

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
| collection `all()` returns the wrong rows (not tested here — it needs a relation model) | silently wrong results |

The rows still come back right for most filters, which is exactly what makes it dangerous — nothing about the response
tells you the endpoint is misconfigured.

**2. Enable the query options you want, and validate.** Every option is off by default and `$top` is capped at zero, so
an unconfigured endpoint rejects every query string:

```csharp
services.AddControllers().AddOData(
   options => options
      .Select()
      .Filter()
      .OrderBy()
      .Count()
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
   AllowedFunctions = SupportedFunctions
};
```

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
| `$expand` | untested | needs a relation model, which the library does not have yet |
| `$search`, `$compute`, `$skiptoken`, `$batch` | untested | — |

`$select` deserves a note: it works and it genuinely narrows the columns queried, despite OData projecting into wrapper
types of its own and despite bug reports to the contrary. `$apply` is the best-supported area of the whole surface.

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
- **Two fixture entities.** `SampleTable` carries only EDM-friendly types, so its model must build cleanly.
  `AwkwardTable` carries the awkward ones, kept separate so a model-building failure would break one test rather than
  the suite.
- **Every test rolls back.** A transaction opens before each test and rolls back after, so the suite runs repeatedly and
  in any order.
- **A second container.** This assembly is isolated from the main integration suite so the OData dependency stays out
  of everything the project ships.

## What is deliberately not covered

- `$expand`. It needs a relation model, which is designed but not yet built. Expansion does work — it maps onto the
  provider's eager-loading path — but it requires an association declared with an explicit foreign-key property, and
  with null-propagation handling left on it returns empty collections silently, having issued the child queries and
  thrown the rows away. Worth knowing before the relation model lands, because that last part is the same
  misconfiguration as above wearing a much worse symptom.
- `$search`, `$compute`, `$skiptoken`, `$batch`.
- Hosting. No controllers, no minimal-API endpoints, no `WebApplicationFactory`. The hosted failure mode above is
  documented, not tested — the in-process suite materializes the queryable itself and so sees a clean exception.
- Frameworks other than `net10.0`. The library's framework coverage is proven by its own build.
