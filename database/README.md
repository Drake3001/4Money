# Internal migration commands - [ef.cs](../tools/ef.cs) file
```bash
dotnet run --file tools/ef.cs -- migrations list

dotnet run --file tools/ef.cs -- migrations has-pending-model-changes

dotnet run --file tools/ef.cs -- migrations add MigrationName

dotnet run --file tools/ef.cs -- database update
```
# Local database restart (from `containers/local`)
```bash
docker compose rm --stop --force postgres-db

docker volume rm zpi4m_zpi4m_pgdata

docker compose up -d --wait postgres-db

dotnet run --file ../../tools/ef.cs -- database update
```
# Database seeders
| Script | Description |
| --- | --- |
| [seed_local.sql](seed_local.sql) | Full sample database |
| [seed_users.sql](seed_users.sql) | Test users only |
