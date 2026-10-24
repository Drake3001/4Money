# Testy backendu

Moduł User ma trzy projekty xUnit, zgodnie z podziałem testów Banking:

- `ZPI4M.User.UnitTests`: tokeny, podpis i zawartość JWT, normalizacja i walidacja DTO.
- `ZPI4M.User.Tests`: kontrakt HTTP kontrolerów z atrapami serwisów, walidacja,
  autoryzacja, identyfikacja właściciela i mapowanie błędów na ProblemDetails.
- `ZPI4M.User.IntegrationTests`: prawdziwy PostgreSQL, repozytoria, serwisy,
  kontrolery i uwierzytelnianie JWT; tylko wysyłka SMTP jest zastępowana atrapą.

Uruchom z katalogu `backend/ZPI4M.Backend`:

```powershell
dotnet test ZPI4M.Backend.slnx --configuration Release
```

Testy integracyjne wymagają zmiennej `USER_TEST_POSTGRES` z connection stringiem
do **jednorazowego serwera testowego**. Konto musi mieć uprawnienia CREATE DATABASE.
Przykład lokalnego uruchomienia:

```powershell
docker run --detach --rm --name zpi4m-user-tests --publish 127.0.0.1:55439:5432 --env POSTGRES_USER=backend_tests --env POSTGRES_PASSWORD=backend_tests_local --env POSTGRES_DB=postgres postgres:15-alpine
$env:USER_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=backend_tests;Password=backend_tests_local'
dotnet test ZPI4M.Backend.slnx --configuration Release
docker stop zpi4m-user-tests
```

Każdy przypadek integracyjny tworzy osobną bazę `user_tests_<losowy_guid>` i wykonuje:

1. `database/init/01-schemas.sql` i `database/init/02-tables.sql`.
2. Wszystkie migracje z `AppDbContext`, łącznie z najnowszą; sprawdza brak oczekujących
   migracji i zgodność modelu ze snapshotem.
3. `database/seed_local.sql` z użytkownikami, grupami, wydatkami i danymi bankowymi.

Po teście usuwana jest tylko wygenerowana baza. Baza wskazana w connection stringu
służy do poleceń administracyjnych i nie jest usuwana ani seedowana.
Testy nie używają `EnsureCreated` ani dostawcy EF InMemory.

Bez zmiennej lokalnie pomijane są tylko testy PostgreSQL. W CI brak zmiennej jest
błędem, żeby testy integracyjne nie przeszły przez przypadkowe pominięcie.
Workflow `backend.yml` uruchamia PostgreSQL 15, czeka na healthcheck i przekazuje
connection string. Runner CI wykrywa śledzone projekty `.UnitTests`, `.Tests`
i `.IntegrationTests`; wszystkie projekty User są wpisane do solution, więc krok
Release build kompiluje je przed `dotnet test --no-build --no-restore`.
