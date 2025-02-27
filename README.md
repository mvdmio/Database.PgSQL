# Database.PgSQL

## TODO
A database framework that makes working with Tables, Joins, Partial Selects and Partial Updates easy.
Should also handle database migrations.
Should also have a dotnet tool for creating migrations and possibly other CLI functionality.

Envisioned API:
```
[Table(name="user")]
public partial class UserTable : DbTable
{
  [Column(name="id")
  public long Id { get; set; }

  [Column(name="first_name"]
  public string FirstName { get; set; }

  [Column(name="last_name"]
  public string LastName { get; set; }

  [Column(name="email"]
  public string Email { get; set; }
}

[Table(name="company"]
public partial class CompanyTable : DbTable
{
  [Column(name="id")
  public long Id { get; set; }

  [Column(name="name"]
  public string Name { get; set; }

  [Column(name="address"]
  public string Address { get; set; }
}

[Table(dbName="company_user")]
public partial class CompanyUserTable : DbTable
{
  [Column(name="company_id")
  public long CompanyId { get; set; }

  [Column(name="user_id")
  public long UserId { get; set; }
}

(long Id, string Name)? company = CompanyTable.Find(id: 14).Select(x => (x.Id, x.Name));
IEnumerable<(long Id, string Name, string Address)> companies = CompanyTable.Query(x => x.Name.StartsWith("Acme")).Select(x => (x.Id, x.Name, x.Address));

var acmeUsers = CompanyUserTable
  .Join<CompanyTable>.On((x, y) => x.CompanyId == y.Id).Select(x => (x.Id, x.Name))
  .Join<UserTable>.On((x, y) => x.UserId == y.Id).Select(x => (x.Id, x.Name, x.Email))
  .Where(x => x.CompanyTable.Name = "Acme");
```
