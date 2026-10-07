# Run recipe

The running surface is the `db` tool (`src/mvdmio.Database.PgSQL.Tool`), driven against a throwaway PostgreSQL container. The `SecondarySchema` fixture project serves as the migrations project. `<work>` is an empty scratch folder; `<proof>` is the run's Proof folder.

## Check
```sh
docker info >/dev/null
dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'
```

## Launch
```sh
docker rm -f pgsql-run >/dev/null 2>&1
docker run -d --name pgsql-run -e POSTGRES_PASSWORD=secret -p 55432:5432 postgres:16
```

## Ready
```sh
for _ in $(seq 1 60); do docker exec pgsql-run pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1 && break; sleep 1; done
sleep 2
docker exec pgsql-run psql -q -U postgres -c "CREATE DATABASE proof"
```
Seed `mvdmio.migrations` rows, when the drive needs them, with `docker exec -i pgsql-run psql -U postgres -d proof -v ON_ERROR_STOP=1 < <seed.sql>`.

## Drive
Write `<work>/.mvdmio-migrations.yml`. A command that deletes or writes files (`cleanup`, `pull`) points `migrationsDirectory` and `schemasDirectory` at copies under `<work>`, never at the fixture's own folders:
```yaml
project: <repo>/test/mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema
migrationsDirectory: <work>/Migrations
schemasDirectory: <work>/Schemas
connectionStrings:
  proof: Host=localhost;Port=55432;Database=proof;Username=postgres;Password=secret
```
Then, from `<work>`:
```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project <repo>/src/mvdmio.Database.PgSQL.Tool --framework net10.0 -- migrate latest -e proof
```
Swap in the command under test (`migrate to <id>`, `cleanup`, `pull`). Some sandboxes refuse to run a script whole; the commands above then run one at a time.

## Evidence
Append ` 2>&1 | tee <proof>/<NN>-<name>.txt` to the drive command. Save database state the same way, for example `docker exec pgsql-run psql -U postgres -d proof -c "SELECT identifier, scope FROM mvdmio.migrations ORDER BY identifier"`.

## Clean up
```sh
docker rm -f pgsql-run
```
