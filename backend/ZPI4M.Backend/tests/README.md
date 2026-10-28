# Testy backendu

Moduły User i Notifications mają po trzy projekty xUnit:

- `ZPI4M.User.UnitTests`: tokeny, podpis i zawartość JWT, normalizacja i walidacja DTO.
- `ZPI4M.User.Tests`: kontrakt HTTP kontrolerów z atrapami serwisów, walidacja,
  autoryzacja, identyfikacja właściciela i mapowanie błędów na ProblemDetails.
- `ZPI4M.User.IntegrationTests`: prawdziwy PostgreSQL, repozytoria, serwisy,
  kontrolery i uwierzytelnianie JWT; tylko wysyłka SMTP jest zastępowana atrapą.
- `ZPI4M.Notifications.UnitTests`: walidacja i deduplikacja w NotifyAsync,
  przygotowanie odbiorców, mapowanie inboxa i przekazanie tożsamości/czasu do repozytorium.
  Zapis jest przechwytywany; te testy nie potwierdzają transakcji PostgreSQL.
- `ZPI4M.Notifications.Tests`: kontrakt HTTP z atrapą serwisu, autoryzacja,
  walidacja paginacji, właściciel z claims, DTO i odpowiedzi ProblemDetails.
- `ZPI4M.Notifications.IntegrationTests`: PostgreSQL, migracje i seed, atomowość
  NotifyAsync przy błędzie FK, paginacja, filtry, izolacja odbiorców, równoległy
  odczyt pojedynczy/zbiorczy, FK i usuwanie oraz HTTP z prawdziwym JWT i sesją.

Uruchom z katalogu `backend/ZPI4M.Backend`:

```powershell
dotnet test ZPI4M.Backend.slnx --configuration Release
```

Testy integracyjne User, Banking i Notifications używają jednej zmiennej
`TEST_POSTGRES_CONNECTION_STRING` z connection stringiem
do **jednorazowego serwera testowego**. Konto musi mieć uprawnienia CREATE DATABASE.
Wszystkie moduły mogą używać tego samego serwera testowego,
ponieważ każdy przypadek tworzy własną bazę. Nie podawaj połączenia do produkcji.
Przykład lokalnego uruchomienia:

```powershell
docker run --detach --rm --name zpi4m-user-tests --publish 127.0.0.1:55439:5432 --env POSTGRES_USER=backend_tests --env POSTGRES_PASSWORD=backend_tests_local --env POSTGRES_DB=postgres postgres:15-alpine
$env:TEST_POSTGRES_CONNECTION_STRING = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=backend_tests;Password=backend_tests_local'
dotnet test ZPI4M.Backend.slnx --configuration Release
docker stop zpi4m-user-tests
```

Każdy przypadek integracyjny tworzy osobną bazę `user_tests_<losowy_guid>`
lub `notifications_tests_<losowy_guid>` i wykonuje:

1. `database/init/01-schemas.sql` i `database/init/02-tables.sql`.
2. Wszystkie migracje z `AppDbContext`, łącznie z najnowszą; sprawdza brak oczekujących
   migracji i zgodność modelu ze snapshotem.
3. `database/seed_local.sql` z użytkownikami, grupami, wydatkami i danymi bankowymi.

Fixture Notifications przed migracjami dodaje starsze powiadomienie i sprawdza
zachowanie treści oraz daty po zmianie sent_at → created_at, domyślny typ General
i brak automatycznie przypisanych odbiorców. Seed następnie przywraca znany stan.

Po teście usuwana jest tylko wygenerowana baza. Baza wskazana w connection stringu
służy do poleceń administracyjnych i nie jest usuwana ani seedowana.
Testy nie używają `EnsureCreated` ani dostawcy EF InMemory.

Bez zmiennej lokalnie pomijane są tylko testy PostgreSQL. W CI brak zmiennej jest
błędem, żeby testy integracyjne nie przeszły przez przypadkowe pominięcie.
Workflow `backend.yml` uruchamia PostgreSQL 15, czeka na healthcheck i przekazuje
connection string. Runner CI wykrywa śledzone projekty `.UnitTests`, `.Tests`
i `.IntegrationTests`; wszystkie projekty User i Notifications są wpisane do solution, więc krok
Release build kompiluje je przed `dotnet test --no-build --no-restore`.
