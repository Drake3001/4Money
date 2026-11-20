# Testy backendu

## Reports

Końcowa weryfikacja 2026-11-18: Expense.UnitTests 67/67,
Expense.IntegrationTests 122/122, Reports.UnitTests 182/182 i
Reports.IntegrationTests 19/19, bez błędów i pominięć. Test pełnego hosta
rozszerzono o produkcyjne OpenAPI, minimum obu dat i zakres 1970–2060.
Szczegóły i wyniki całej regresji: [raport weryfikacji](../../../notes/REPORTS_FINAL_VERIFICATION.md).
Cała solucja: 1277 zaliczonych, 0 błędów, 0 pominiętych w 17 projektach,
z PostgreSQL 15 i rzeczywistym testem S3; Release bez błędów i ostrzeżeń.
Wszystkie utworzone kontenery testowe i ich wolumeny usunięto po regresji.

Weryfikacja 2026-11-18 po usunięciu własnego bindera: Reports.UnitTests 133/133,
w tym osobno HTTP/OpenAPI 109/109; Expense.UnitTests 67/67, bez pominięć.
Cała solucja Release skompilowana bez błędów i ostrzeżeń, także zmienione testy
integracyjne. Integracji, PostgreSQL, Dockera i usług zewnętrznych nie uruchamiano.
Expense wymagało zmiennej procesu `Logging__EventLog__LogLevel__Default=None`
po odmowie dostępu do dziennika Windows EventLog.

Weryfikacja 2026-11-17: Reports.UnitTests 129/129, Reports.IntegrationTests
18/18 bez pominięć. Pełna solucja Release z Docker PostgreSQL 15: 1071 zaliczonych,
0 błędów, 1 pominięty ręcznie włączany test S3; końcowa kompilacja bez ostrzeżeń.

`ZPI4M.Reports.UnitTests` sprawdza cztery aktualne raporty na atrapach
IExpenseAggregationService i wyłącznie metadanych kategorii IExpenseService.
Obejmuje przekazywanie filtrów, puste kategorie, top 8/Pozostałe, wspólny zestaw
serii, zera, progi 24/25/48/49 miesięcy kalendarzowych, średnie dni tygodnia,
rok przestępny, minimalną datę 1970-01-01, górną granicę typu DateOnly,
anulowanie i błędy odczytu. Wspólna walidacja odrzuca wcześniejsze i odwrócone
daty; wieloletnie zakresy są akceptowane dla wszystkich czterech endpointów.
ReportsApiContractTests używa rzeczywistego ReportsService, HttpCurrentUser
i kontrolera na TestServer. Sprawdza cztery docelowe trasy GET, brak aliasów,
standardowe wiązanie Guid: brak `categoryIds` daje pusty raport (200), pojedyncze,
powtarzane i indeksowane UUID są akceptowane, puste/błędne/mieszane wartości
dają 400. Sprawdza też walidację pozostałych parametrów i ProblemDetails.
Generuje też dokument OpenAPI z produkcyjnym transformerem linkowanym z hosta
API i sprawdza pięć wspólnych parametrów oraz opcjonalną tablicę UUID.
Ten projekt nie sprawdza PostgreSQL ani rzeczywistego JWT.

```powershell
dotnet test tests/ZPI4M.Reports.UnitTests/ZPI4M.Reports.UnitTests.csproj --configuration Release
```

`ZPI4M.Reports.IntegrationTests` sprawdza HTTP → ReportsService →
ExpenseAggregationService → ExpenseRepository/Grouping → PostgreSQL.
ReportsWorkflowTests korzysta z rzeczywistych agregacji SQL, repozytoriów
i HttpCurrentUser. TestServer ma testowe uwierzytelnianie oraz adapter wyłącznie
metadanych IExpenseService, który czyta kategorie z rzeczywistego CategoryRepository.
Nie zastępuje agregacji ani uprawnień atrapami i nie uruchamia OCR/S3/SMTP.

ReportsProductionHostTests uruchamia dodatkowo pełny produkcyjny host API jako
proces dotnet/Kestrel, na wolnym porcie localhost i własnej bazie fixture.
ProjectReference do API zapewnia jego kompilację. Host używa rzeczywistego DI,
IExpenseService, logowania seed_anna, podpisanego JWT i sprawdzania sesji.
Wykonuje 17 definicji GET z kolekcji Postman przez HttpClient, generuje OpenAPI,
sprawdza puste/błędne kategorie, brak starej trasy oraz odrzucenie JWT po logout.
Nie jest to uruchomienie Postman CLI. Konfiguracja jest syntetyczna, klucze JWT/OCR
losowane w procesie, a cwd to własny katalog tymczasowy bez lokalnego .env.
Host jest zatrzymywany przed usunięciem bazy. S3/OCR/SMTP mają lokalne nieczynne
adresy; te funkcje nie są wywoływane przez weryfikowane raporty.

Projekt linkuje istniejący `ZPI4M.Expense.IntegrationTests/PostgresFixture.cs`:
każdy przypadek dostaje osobną bazę `expense_tests_<guid>`, SQL init, migracje
i syntetyczny seed. Zmiany danych dotyczą wyłącznie tej bazy, usuwanej po teście.
Nie wymaga Testcontainers ani dodatkowych pakietów do zarządzania Dockerem.
Ustaw `TEST_POSTGRES_CONNECTION_STRING` do jednorazowego serwera testowego
według instrukcji PostgreSQL poniżej. Bez zmiennej lokalnie testy są pomijane;
w CI jej brak jest błędem. Nowy projekt jest w solucji i pasuje do istniejącego
filtra `.IntegrationTests.csproj` w CI.

Zakres obejmuje cztery raporty osobiste i grupowe, udziały i płatnika bez udziału,
filtry kategorii, puste okresy, odmowę dostępu do cudzych i nieistniejących grup
(także przy pustym wyborze), byłego członka, włączne granice dat (także ostatnią
mikrosekundę dnia), rok przestępny, lata 1/9999, ujemne kwoty, grosze i Pozostałe
oraz progi 24/25/48/49 miesięcy i średnie uwzględniające dni bez wydatków.
Wartości oczekiwane są zapisane na podstawie stałych danych syntetycznych;
obejmują również grosze i ujemne pozycje kategorii.

```powershell
dotnet test tests/ZPI4M.Reports.IntegrationTests/ZPI4M.Reports.IntegrationTests.csproj --configuration Release
```

Jeśli testy HTTP Expense na Windows zgłoszą brak uprawnień do dziennika zdarzeń
`.NET Runtime`, można dla procesu testowego wyłączyć ten provider bez zmiany kodu:
`$env:Logging__EventLog__LogLevel__Default = 'None'`.

## User i Notifications

Moduły User i Notifications mają po trzy projekty xUnit:

- `ZPI4M.User.UnitTests`: tokeny, podpis i zawartość JWT, normalizacja i walidacja DTO,
  szablony HTML i walidacja adresu frontendu w linkach e-mail.
  Szablon pierwszego hasła Google korzysta z tego samego formularza i akcji
  `reset` co reset istniejącego hasła.

- `ZPI4M.User.Tests`: kontrakt HTTP kontrolerów z atrapami serwisów, walidacja,
  autoryzacja, identyfikacja właściciela i mapowanie błędów na ProblemDetails.
- `ZPI4M.User.IntegrationTests`: prawdziwy PostgreSQL, repozytoria, serwisy,
  kontrolery i uwierzytelnianie JWT; SMTP, storage i zewnętrzna tożsamość Google
  są zastępowane atrapami.
- `ZPI4M.Notifications.UnitTests`: walidacja i deduplikacja w NotifyAsync,
  przygotowanie odbiorców, mapowanie inboxa i przekazanie tożsamości/czasu do repozytorium.
  Zapis jest przechwytywany; te testy nie potwierdzają transakcji PostgreSQL.
- `ZPI4M.Notifications.Tests`: kontrakt HTTP z atrapą serwisu, autoryzacja,
  walidacja paginacji, właściciel z claims, DTO i odpowiedzi ProblemDetails.
- `ZPI4M.Notifications.IntegrationTests`: PostgreSQL, migracje i seed, atomowość
  NotifyAsync przy błędzie FK, paginacja, filtry, izolacja odbiorców, równoległy
  odczyt pojedynczy/zbiorczy, FK i usuwanie oraz HTTP z prawdziwym JWT i sesją.

Testy User.IntegrationTests obejmują rollback rejestracji przy awarii/anulowaniu
SMTP, niewidoczność nowego konta przed przyjęciem wiadomości, ponowienie
rejestracji oraz zachowanie poprzedniego tokenu przy nieudanej ponownej wysyłce
dla potwierdzenia e-maila, zmiany e-maila, resetu hasła i dezaktywacji.
Wysyłka jest atrapą; testy nie wysyłają rzeczywistych wiadomości.
Rejestracja Google wymagająca potwierdzenia adresu ma osobny kontrakt:
awaria SMTP po commicie zachowuje konto, grupę i token; testy Google sprawdzają
ponowną wysyłkę. Reset i ustawienie pierwszego hasła zatwierdzają token
dopiero po przyjęciu wiadomości przez atrapę SMTP.
Limits ma osobny `ZPI4M.Limits.UnitTests`: kontrakt 10 endpointów CRUD,
uwierzytelnienie i ICurrentUser, odpowiedzi 501, wiązanie DTO/paginacji oraz
OpenAPI. TestServer działa bez bazy i serwisów biznesowych; testy nie potwierdzają
docelowych uprawnień grupowych ani zapisu limitów.

Uruchom z katalogu `backend/ZPI4M.Backend`:

```powershell
dotnet test ZPI4M.Backend.slnx --configuration Release
```

Testy integracyjne User, Banking, Expense i Notifications używają jednej zmiennej
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

Każdy przypadek integracyjny tworzy osobną bazę z prefiksem `user_tests_`,
`banking_tests_`, `expense_tests_` lub `notifications_tests_` i losowym GUID.
Fixture pełnego przepływu wykonuje:

1. `database/init/01-schemas.sql` i `database/init/02-tables.sql`.
2. Wszystkie migracje z `AppDbContext`, łącznie z najnowszą; sprawdza brak oczekujących
   migracji i zgodność modelu ze snapshotem.
3. `database/seed_local.sql` z użytkownikami, grupami, wydatkami i danymi bankowymi.

`StarterGroupTests` w User.IntegrationTests sprawdza trigger tworzący zwykłą
grupę „Wydatki osobiste” w PLN: rejestrację przed potwierdzeniem e-maila,
członkostwo administratora, rollback błędu członkostwa i transakcji nadrzędnej,
brak backfillu, Up/Down/Up oraz ponowne wykonanie seeda. Seed po tej migracji
zawiera pięć grup startowych i dwa dotychczasowe przykłady współdzielone.

Testy Google nie wymagają prawdziwego ClientId ani konta Google:

- `GoogleAuthenticationTests` w UnitTests waliduje tokeny podpisane lokalnym RSA
  z atrapą HTTPS discovery/JWKS: podpis, wystawcę, audience, expiry, nonce i pola
  tożsamości, a także brak metadanych i anulowanie. Sprawdza konfigurację, DTO,
  powiązanie challenge z przeglądarką, jego termin i jednorazowość współbieżną.
- `GoogleControllerTests` w Tests sprawdza statusy/kody błędów, no-store,
  HttpOnly/Secure/SameSite cookie, walidację, disabled/unavailable oraz wspólny
  limit logowania hasłem i Google. Atrapy nie potwierdzają zapisu bazy.
- `GoogleFlowTests` w IntegrationTests używa prawdziwych migracji, triggera,
  repozytoriów, serwisów i JWT. Atrapa Google sprawdza nonce oczekiwane przez
  konkretny ID token. Scenariusze obejmują rejestrację bez hasła, potwierdzenie
  adresów zewnętrznych, pierwsze hasło, obie metody logowania, unieważnienie sesji,
  blokadę zmiany e-maila, brak łączenia po e-mailu, nieaktywne konta, cookie replay,
  równoległe próby, rollback błędu sesji razem z grupą, awarię SMTP i resend,
  unikalność sub także dla nieaktywnych kont, CHECK metody logowania i Up/Down/Up
  migracji Google wraz z odmową Down dla kont bez hasła.

Nie są to testy rzeczywistego okna Google Identity Services ani dostarczenia
wiadomości przez SMTP. Frontend nie jest jeszcze zintegrowany; ręczny przepływ
opisuje [Postman](../../../postman/README.md#google).

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

## Zdjęcia profilu

UserIconTests sprawdza typ i sygnaturę PNG/JPEG/WebP, dokładny limit 5 MiB,
puste/błędne pliki, konfigurację katalogu presetów, podpisane URL-e, anulowanie
i sprzątanie nieudanego uploadu. UserIconControllerTests obejmuje autoryzację,
no-store, mapowanie błędów, multipart, pola tylko serwerowe i routing PUT
z obsługą JSON/multipart oraz bez Content-Type.

UserIconFlowTests używa prawdziwego PostgreSQL i RecordingObjectStorage:
upload → zachowanie → preset → usunięcie, izolację właściciela, błędy walidacji,
rollback bazy, błędy uploadu/usuwania, równoległe podmiany, niepewny commit
przed/po faktycznym zatwierdzeniu oraz Up/Down/Up i CHECK migracji ikon User.
Nie jest to test rzeczywistego S3; adapter ma własne testy Shared oraz ręczny
test S3 Receipt opisany poniżej. Brak konfiguracji PostgreSQL pomija testy bazy.

## Receipt

`ZPI4M.Receipt.UnitTests` sprawdza serwis Receipt, klienta HTTP OCR
oraz walidację konfiguracji OCR; adapter S3 i jego konfigurację sprawdza
`ZPI4M.Shared.UnitTests`. `CreateAsync` wymaga jawnego `runOcr` i zwraca
`ReceiptCreationResult` (ID oraz opcjonalny pełny wynik OCR). Testy obejmują
oba tryby: analizę i zapis oraz samo zdjęcie z `ExtractionResult = null`
i bez żądania HTTP do OCR. Wspólne scenariusze obejmują strumienie bez przewijania,
odczyt od bieżącej pozycji, walidację, anulowanie, błędy uploadu i zapisu,
sprzątanie plików tymczasowych oraz niepewne potwierdzenie zapisu.
Testy usuwania sprawdzają obsługę błędów.
OCR i dostawcy są zastępowani atrapami; testy nie wywołują płatnego modelu.
Receipt nie ma własnych endpointów; endpointy Expense korzystają z jego serwisu.
Testy HTTP tej integracji znajdują się w Expense.IntegrationTests.

Z katalogu `backend/ZPI4M.Backend`, po kompilacji Release:

```powershell
dotnet test tests/ZPI4M.Receipt.UnitTests/ZPI4M.Receipt.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/ZPI4M.Receipt.IntegrationTests/ZPI4M.Receipt.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

`ZPI4M.Receipt.IntegrationTests` zawiera testy pomocnika sprzątania i ręczny test
rzeczywistego S3: upload syntetycznego obrazu, podpisany GET, odmowa anonimowego
GET i dwukrotne usunięcie. Test S3 wymaga działającego lokalnego magazynu,
konfiguracji `STORAGE__*` z `containers/local/.env` lub środowiska i domyślnych appsettings oraz
`STORAGE_INTEGRATION_TESTS=1`. Bez tej flagi jest pomijany. Nie potwierdza
pełnego przepływu OCR → S3 → PostgreSQL ani integracji z Expense.
## Model grup, wydatków i rozliczeń

`ZPI4M.Banking.IntegrationTests/GroupingModelTests.cs` używa tego samego
izolowanego fixture PostgreSQL: SQL baseline, migracje, kontrolowany seed PLN,
Up/Down/Up, odrzucenie historycznych danych przed DDL, CHECK/UNIQUE/FK,
agregaty seeda, domyślne wartości i odczyty repozytoriów. Każdy przypadek
ma osobną bazę banking_tests_<guid>. `GroupingModelContractTests` sprawdza
zgodność snapshotu i modelu oraz strukturę migracji bez łączenia z PostgreSQL.
Nie traktuj pominiętych testów bazy jako potwierdzenia migracji lub seeda.

Storage: testy adaptera i konfiguracji znajdują się w ZPI4M.Shared.UnitTests.
Test SeaweedFS pozostaje w Receipt.IntegrationTests (STORAGE_INTEGRATION_TESTS=1).

## Expense

Expense.UnitTests obejmuje obliczenia, walidację, kontrakt HTTP/OpenAPI oraz
rozdzielenie rejestracji usług Expense od Grouping i User. Expense.IntegrationTests tworzy izolowaną
bazę dla każdego testu z TEST_POSTGRES_CONNECTION_STRING. Zawiera rzeczywisty
przepływ HTTP kategorii → dostępnych źródeł → preview → zapis → szczegóły,
walidację źródeł, wykluczenie zajętych transakcji przed paginacją, wyścig dwóch
zapisów, rollback i migrację wersji. Host HTTP używa testowego uwierzytelniania,
rzeczywistych serwisów/repozytoriów Expense, Banking, User i Grouping.
ExpenseWorkflowTests obejmuje historię, edycję, usuwanie i paragony z atrapami
OCR/storage. ExpenseAggregateTests sprawdza wspólny commit/rollback, dokładne
grosze, przepełnienie oraz wyścigi z rozliczeniami i zmianami grupy. ExpenseAdded
jest sprawdzane pod kątem odbiorców, braku odbiorców i rollbacku przed/po zapisie
powiadomienia. Test HTTP sprawdza też rollback paragonu i cleanup uploadu po błędzie
powiadomienia oraz zachowanie starej wiadomości po usunięciu wydatku. Test HTTP
używa rzeczywistych usług Grouping; jawne atrapy służą scenariuszom błędów.
Nie uruchamia Tink, OCR ani storage. Nie jest testem nowych zachowań JWT.

Nie kieruj fixture do bazy aplikacji; konto testowe potrzebuje tworzenia/usuwania
oddzielnych baz. Testy nie wykonują seeda na skonfigurowanej bazie serwera,
wyłącznie we własnych losowo nazwanych bazach. Brak konfiguracji lokalnie oznacza
pominięcie testów PostgreSQL, nie potwierdzenie powodzenia przepływu.

ExpenseAggregationTests sprawdza cztery bieżące agregacje SQL, obie daty włącznie,
skrajne daty i infinity, dostęp byłego członka, puste kategorie, udziały,
rozdzielanie groszy, ujemne/zerowe pozycje, rabaty i ilości ułamkowe.
Oczekiwane kwoty testu finansowego są jawne dla stałych danych syntetycznych;
nie zależą od starszego serwisu odczytu i jego kalkulatora, usuniętych 2026-11-18.
Usunięto testy dotyczące wyłącznie tego nieużywanego serwisu i kalkulatora.
Kontrakt: [Expense dla Reports](../../../notes/EXPENSE_REPORTS.md).
