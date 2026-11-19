# Praca w repozytorium

Właściciel poleceń uruchomienia, konfiguracji i kontroli. Reguły modelu opisuje
[domena](domain.md), transakcje [architektura](architecture.md), faktyczny zakres
implementacji i dowody testów [status](implementation-status.md). Instrukcja nie
jest potwierdzeniem uruchomienia każdej komendy; operacje na bazie wykonuj tylko
w zakresie zadania. Wszystkie przykłady zakładają katalog główny repozytorium,
chyba że wyraźnie podano inny katalog roboczy.

## Wymagania i konfiguracja

Backend wymaga SDK .NET 10, lokalny stos Docker/Compose, narzędzia Python i uv.
Frontend bez Dockera używa Node.js 22.12+ z linii 22. Worker ma osobny projekt
Python 3.12/uv. Wersje pakietów i narzędzi sprawdzaj w manifestach zamiast
przepisywać je do każdej instrukcji. Narzędzia EF i diagramów są w głównym
manifestcie dotnet-tools; po clone lub jego zmianie wykonaj `dotnet tool restore`.
Diagramy opisuje [tools](../tools/README.md#architecture-diagrams-projgraph).

| Konfiguracja | Przeznaczenie |
| --- | --- |
| `containers/local/.env` | Jedyny plik środowiska całego lokalnego Compose |
| `backend/ZPI4M.Backend/src/ZPI4M.Api/.env` | API uruchomione bez Dockera |
| `workers/extraction_worker/.env` | Worker uruchomiony bez Dockera |
| `appsettings.json` hosta | Niesekretne wartości domyślne i opcje usług |
| `.env.example` właściwego programu | Szablon; nie kopia realnych sekretów |

Zachowuj istniejące lokalne pliki; nie nadpisuj ich szablonem. Host wywołuje
Env.Load przed zbudowaniem konfiguracji, następnie dodaje środowisko. Lokalny
.env może nadpisać wcześniej ustawioną zmienną procesu o tej samej nazwie.
Przy diagnozie sprawdź efektywną konfigurację bez wypisywania poświadczeń.
ASP.NET używa `__` dla zagnieżdżonych opcji, ale OCR/STORAGE są istniejącymi
płaskimi kluczami. Nie przenoś ich samodzielnie do nowej sekcji JSON.

Start API wymaga poprawnej bazy, klucza JWT, opcji storage, klucza dowodu OCR
oraz FRONTEND_URL. Walidacja storage działa także przy pracy tylko nad kontem;
brak uploadu w zadaniu nie wyłącza wymagań startowych. SMTP jest sprawdzany przy
wysyłce, więc sam start hosta nie potwierdza działania skrzynki. FRONTEND_URL
musi być absolutnym HTTP/HTTPS bez danych logowania, query i fragmentu i wskazywać
frontend dostępny odbiorcy maila. Szczegóły JWT, Google, SMTP i ikon są w
[User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md).

## Lokalna baza i API

Compose uruchamia frontend Vite, PostgreSQL, backend, SeaweedFS i OCR. Domyślne
[mapowania portów](../containers/local/README.md) to frontend 5173, API 5283,
baza 5434, S3 8333 i OCR 8000. Adres bazy wewnątrz kontenera różni się od adresu
na hoście. Compose ustawia zależności nazwami usług; localhost w kontenerze oznacza
ten kontener, nie komputer użytkownika ani sąsiednią usługę.

Uzupełnij lokalne `.env` zgodnie z szablonem, w tym własne POSTGRES_USER i
POSTGRES_PASSWORD. Przykładowy start samej bazy:

```bash
docker compose --env-file containers/local/.env -f containers/local/docker-compose.yml up -d postgres-db
```

Nowy pusty wolumen wykonuje skrypty `database/init/`; restart istniejącego tego
nie robi. Zmiana loginu/hasła w .env nie zmienia danych logowania w istniejącym
wolumenie. Nie kasuj go przy błędzie uwierzytelniania. Baza lokalna na hoście to
`localhost:5434`, domyślnie `zpi4m_db`. Loginy PostgreSQL są odrębne od kont
aplikacji tworzonych przez seed.

Nowa baza wymaga kolejności **SQL init → migracje EF → opcjonalny seed**.
Migracje i seed są osobnymi krokami, nie częścią zwykłego restartu. Sam seed
usuwa dane, w tym sesje i powiązania bankowe, i wymaga aktualnego schematu.
Nie uploaduje obrazów ani nie wywołuje OCR/Tink. Odpowiedź pod podpisanym URL
syntetycznego paragonu może więc wskazywać nieistniejący plik.

Pełny stos po przygotowaniu schematu:

```bash
docker compose --env-file containers/local/.env -f containers/local/docker-compose.yml up --build -d
```

Przy API bez Dockera ustaw DB_LOCAL_CONNECTION_STRING do bazy na hoście;
poświadczenia przechowuj tylko lokalnie. Z katalogu `backend/ZPI4M.Backend/src/ZPI4M.Api`:

```bash
dotnet run --launch-profile http
```

Profil http używa 5283, https także 7278; oba są Development. Bez profilu adres
i środowisko pochodzą z pozostałej konfiguracji. OpenAPI w Development jest pod
`/openapi/v1.json`, bez wbudowanego Swagger UI. Postman baseUrl, frontend proxy
i callback Tink muszą odpowiadać rzeczywiście uruchomionemu profilowi. Samo
.env z adresem callbacku nie zmienia rejestracji aplikacji po stronie dostawcy.

Do obsługi logowania i inboxa nie potrzeba wykonującego OCR workera, ale nadal
potrzeba poprawnych opcji hosta. S3 jest potrzebne operacjom na plikach. Nie
włączaj prawdziwych wywołań integracyjnych, jeśli zadanie wymaga tylko testu
lokalnej walidacji lub dokumentacji.

## Frontend — uruchomienie i kontrole

React 19, TypeScript, Vite 8 i Tailwind CSS 4.
Bez Dockera używaj Node.js 22.12+ z linii 22, zgodnie z README frontendu.
Z katalogu `frontend/`:

```bash
npm ci
npm run dev
npm run build
npm test
```

W PowerShell można użyć `npm.cmd`. Build wykonuje typecheck obu projektów
TypeScript i zapisuje `dist/`. `npm test` uruchamia testy Node z Vite,
syntetycznym HTTP, renderowaniem serwerowym i kontrolą logiki formularzy;
nie uruchamia przeglądarki ani rzeczywistych usług. Osobny pakiet konwersji
HEIC ładuje się na żądanie i powoduje ostrzeżenie rozmiaru podczas builda.
Workflow backendu nie wykonuje testów ani builda frontendu.

Domyślnie przeglądarka wysyła `/api` do proxy dev/preview: `API_PROXY_TARGET`
wskazuje `http://localhost:5283`, w Compose `http://backend:8080`.
`VITE_API_BASE_URL` nadpisuje adres w przeglądarce i przy innym origin wymaga
CORS, którego host obecnie nie rejestruje. Nie umieszczaj sekretów w `VITE_*`.
Produkcja wymaga osobnego serwowania `dist/` i routingu `/api`; lokalny obraz
Compose uruchamia Vite. Mobile/Capacitor nie ma jeszcze własnego projektu.
Szczegóły: [frontend](../frontend/README.md), [Compose](../containers/local/README.md).

## Modele i migracje

Zmiana modelu obejmuje konfigurację modułu oraz wspólny AppDbContext. Generuj
migrację, designer i snapshot razem. Przejrzyj DDL, w szczególności FK/CHECK
istniejące w bazowym SQL, których nie zna EF. Sprawdzenie zgodności snapshotu
nie dowodzi zachowania tych ograniczeń podczas Up/Down.

```bash
dotnet tool restore
dotnet run --file tools/ef.cs -- migrations has-pending-model-changes
dotnet run --file tools/ef.cs -- migrations add NazwaZmiany
```

Wrapper wybiera Database, startup Api i AppDbContext, uruchamiając EF w katalogu
API. `has-pending-model-changes` porównuje model bez stosowania migracji; `add`
zmienia pliki. `migrations list` może łączyć się z bazą i ładuje .env hosta.
`database update` zmienia bazę. Do wyboru innego połączenia dla operacji bazowych
użyj jawnego `--connection`, nie zakładaj pierwszeństwa zmiennej nad .env.
Szczegóły wrappera i jawnego wyboru wersji narzędzia: [tools](../tools/README.md#ef-core-migrations).

Aktualną końcówką łańcucha jest:

| Migracja | Znaczenie dla kolejnej zmiany |
| --- | --- |
| `20261102215301_AddGroupingModel` | Zatwierdzone grupy i członkostwa; osłony danych |
| `20261103165622_CompleteExpenseModel` | Płatnik, udziały procentowe, pozycje i wersje; wymaga pustych wydatków przy zmianie schematu |
| `20261103203528_AddBankTransactionAssignments` | Wspólny rejestr konsumentów transakcji |
| `20261109191600_UserImprovements` | Aktywny e-mail, Google, ikony i trigger grupy startowej |
| `20261116211631_NormalizeExpenseCategoriesAndAddDeposit` | Normalizacja słownika i Kaucja |
| `20261119182900_AddLimits` | Końcowy model Limits po wspólnej migracji normalizacji |

AddLimits blokuje legacy group_limit i category_group_limit i wymaga
ich pustych przed DDL. To jedyna migracja Limits dodana w PR; zastępuje dawną
`20261117155636_AddBudgetDefinitions`. Tworzy finalne definicje oraz alert-state;
wykluczenia obejmują wyłącznie włączone okresy 1/2/3, a Custom mogą się nakładać.
Down wymaga
pustych wszystkich czterech końcowych tabel Limits. Nie usuwa ani nie konwertuje
danych. Rola migracyjna musi móc utworzyć btree_gist; Down zachowuje rozszerzenie,
które może być współdzielone. DDL ma dotyczyć tylko Limits i tego rozszerzenia.

Bazy, które zastosowały usunięte migracje gałęzi, nie mogą po prostu wykonać
nowej migracji na istniejącym schemacie. Należy wrócić do odpowiadającej im
wspólnej bazy używając starych plików i uwzględniając ich osłony, albo uzgodnić
osobno odtworzenie danych lokalnych. Nie usuwaj samych wpisów historii EF i nie
używaj seeda jako naprawy. Historyczne migracje wspólne i SQL init pozostają
bazą, nie materiałem do dowolnego przepisywania przy nowej funkcji.

Przy kolejnym rebase sprawdź rzeczywistą granicę wspólnych migracji. Jeśli model
bazowy się zmienił, wygeneruj ponownie designer/snapshot konsolidowanej migracji,
porównaj DDL i sprawdź round-trip na pustej bazie oraz odmowę utraty danych.
Przy zmianie timestampów sprawdź znaczenie starych wartości, nie tylko typ C#.
Pliki migracji nie są dowodem zastosowania ich w bazie deweloperskiej.
Instrukcja pomocnicza: [migracje](../notes/MIGRATIONS.md).

## Kontrole backendu

Poniższe polecenia wykonuj z katalogu głównego repozytorium:

```bash
dotnet restore backend/ZPI4M.Backend/ZPI4M.Backend.slnx
dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --configuration Release --no-restore
dotnet format style backend/ZPI4M.Backend/ZPI4M.Backend.slnx --severity warn --exclude-diagnostics IDE0005 --verify-no-changes --no-restore
dotnet format whitespace backend/ZPI4M.Backend/ZPI4M.Backend.slnx --verify-no-changes --no-restore
```

Restore, build i test zapisują artefakty. `--no-build` zakłada wcześniejszy udany
build tej konfiguracji. Polecenia formatowania z `--verify-no-changes`
sprawdzają styl bez poprawiania źródeł. Ustawienia są w
[EditorConfig](../backend/.editorconfig).

Pomocnik CI uruchamia osobne zestawy na podstawie nazw śledzonych projektów:

```bash
bash tools/ci/backend_checks.sh detect
bash tools/ci/backend_checks.sh unit --results .cache/backend-results
bash tools/ci/backend_checks.sh integration --results .cache/backend-results
```

Użyj świeżego katalogu wyników przy kolejnych uruchomieniach. Pomocnik wybiera
śledzone projekty `.UnitTests.csproj` i `.Tests.csproj` dla zestawu unit oraz
`.IntegrationTests.csproj` dla integration. Solucja zawiera po trzy projekty
User, Banking i Notifications, po dwa Receipt, Expense, Reports i Limits oraz Grouping i Shared (łącznie dziewiętnaście projektów). Komunikat
„not configured” nie oznacza wykonania testów.

Workflow backendu uruchamia PostgreSQL i ustawia `TEST_POSTGRES_CONNECTION_STRING`
dla integracyjnych testów User, Banking, Notifications i Expense. Wszystkie zestawy tworzą
własne bazy o losowych nazwach na serwerze testowym.
Host nadal ładuje `.env` przez `Env.Load()`; testy HTTP używają własnych hostów.

Receipt.IntegrationTests zawiera ręczny test rzeczywistego S3, niezależny od
PostgreSQL. Wymaga działającego magazynu i `STORAGE_INTEGRATION_TESTS=1`;
odczytuje ustawienia `STORAGE_*` z lokalnego `.env` API oraz zmiennych środowiskowych.
Tworzy i usuwa syntetyczny obiekt. Nie włączaj go przy samej kontroli dokumentacji.
Obecny workflow backendu nie uruchamia SeaweedFS, workera ani testów pytest.

## Pozostałe kontrole i integracje

Dobieraj kontrolę do zachowania, nie tylko projektu, w którym zmieniono plik.
Zmiana zapisu Expense/Limits może wymagać regresji Grouping, Banking i inboxa.
Testy z bazą używają disposable serwera i własnych losowych baz; nie podawaj
DB_LOCAL_CONNECTION_STRING jako połączenia testowego. Lokalny brak konfiguracji
pomija PostgreSQL, w CI jest błędem. Raport „not configured” lub skipped nie
jest zaliczeniem testu. Instrukcje: [tests](../backend/ZPI4M.Backend/tests/README.md).

Dla dokumentacji wystarczy kontrola zgodności, odsyłaczy i diffu; nie wymaga
kontenerów, seeda, migracji ani testów API. Dla kodu wykonaj build i odpowiednie
testy. Dla schematu dodatkowo Up/Down na odizolowanej bazie i testy osłon.
Operacje infrastruktury wykonuj zgodnie z [Proxmox](../proxmox-infra/README.md)
i właściwym workflow; merge do master może uruchamiać wdrożenie.

LimitsAlertsSweep ocenia bieżące kalendarze i aktywne dziś Custom, nie odnawia
definicji. Opcje i limity walidowane przy starcie są w [Limits](../backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md#alert-sweep).
Zmiana .env wymaga restartu API lub odtworzenia kontenera backendu. Odczyt GET
nie służy do uruchamiania alertów ani naprawiania stanu.

## Worker OCR i magazyn obrazów

Do uruchomienia workera poza Dockerem potrzebne są Python 3.12, uv co najmniej
0.12.23 (wymaganie `workers/extraction_worker/pyproject.toml`) oraz lokalny
`OPENROUTER_API_KEY`. Instrukcja i kontrakt znajdują się w
[README workera](../workers/extraction_worker/README.md); eksperymenty i ewaluacja
w [osobnej instrukcji](../workers/extraction_worker/experiments/README.md).
Z katalogu `workers/extraction_worker`:

```bash
uv sync --locked
uv run --env-file .env uvicorn api:app --host 127.0.0.1 --port 8000 --workers 1
```

Start rozgrzewa OCR i może pobierać brakujące modele. Wywołania ekstrakcji korzystają
z dostawcy zewnętrznego; `/health` nie potwierdza jakości OCR ani dostępu do modelu.
Testy korzystają z atrap OCR i dostawców; z tego samego katalogu:

```bash
uv run --extra test pytest -q --basetemp .cache/test-temp/pytest
```

Host .NET waliduje opcje OCR i S3 przez `ValidateOnStart`. Oprócz JWT wymaga
poprawnych `STORAGE_ACCESS_KEY` i `STORAGE_SECRET_KEY` oraz pozostałych opcji S3.
Domyślne opcje są w `appsettings.json`; API `.env` zawiera poświadczenia.
W Compose używane jest wyłącznie `containers/local/.env` i adresy usług wewnętrznych.
`STORAGE_PUBLIC_ENDPOINT` musi być dostępny dla odbiorcy podpisanego URL;
domyślne `http://localhost:8333` odpowiada lokalnemu mapowaniu portu S3.
Po zmianie portu lub przy dostępie z innego urządzenia dopasuj ten adres.

Kolekcja Storage w Postmanie sprawdza bezpośrednio S3; nie wymaga backendu ani JWT
i nie potwierdza integracji Receipt. Analiza i odczyt paragonu są dostępne przez
endpointy Expense, a jego zapis przez POST/PUT formularza wydatku.
Weryfikację z rzeczywistym OCR/storage opisuje [status](implementation-status.md).

## Postman i dane syntetyczne

[Kolekcje Postmana](../postman/README.md) korzystają z JWT konta aplikacji, nie
sekretu aplikacji Tink. Tokeny z maili wkleja się ręcznie; Runner User nie jest
automatycznym testem całego konta. Bezpośrednia kolekcja S3 potwierdza magazyn,
nie uprawnienia ani transakcję zapisu Receipt przez Expense.

Instrukcje sandboxa, Connector v2, manifestów, importu profili i ponownego
powiązania użytkownika po resecie są w [Tink Postman](../notes/TINK_POSTMAN.md).
Wybierz operację importu/powiązania zamiast resetu całej bazy, jeśli zadanie tego
wymaga. Zwykłe ponowienie connection-attempt nie wymaga nowej rejestracji konta.
Dane dostawcy, zgody i mapowanie TinkUserId są oddzielne od lokalnego seeda.
Eksporty muszą być syntetyczne i nie zawierać access/refresh tokenów.

## Diagnostyka Banking

503 lub ogólny polski komunikat UI nie wskazuje sam źródła błędu. Frontend
pobiera konta/połączenia/zgody po loginie, a nie tylko po kliknięciu „Połącz bank”.
Identyczne alerty mogą zostać scalone bez ponowienia żądania. Ustal konkretny
endpoint, status i etap procesu oraz bezpieczny kod błędu.

BANKING_TINK_UNAVAILABLE jest szerokim kodem obejmującym sieć, timeout, limity,
błędy dostawcy oraz lokalne continuous_access_unverified. Dostęp zabroniony
mapuje się na ACCESS_DENIED, nieprawidłowa odpowiedź na INVALID_DATA.
Sprawdź efektywną konfigurację procesu, nie sam szablon Compose. Nie ustawiaj
ContinuousAccessVerified=true jako obejścia: flaga nie nadaje uprawnień Tink.
Instrukcja integracji i ograniczeń: [Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md),
[Tink](../notes/TINK.md). Nie publikuj tokenów, danych finansowych ani pełnych
adresów autoryzacyjnych w raporcie diagnozy.

## Hooki i konwencje Git

Z katalogu głównego, przy potrzebie konfiguracji lokalnych hooków:

```bash
uv sync --project tools --only-group pre-commit --locked
uv run --project tools --only-group pre-commit --locked python tools/hooks/install.py
```

Instalacja zmienia lokalne środowisko i hooki Git; nie jest wymagana do samego
czytania dokumentacji. Instalator wykrywa konflikty istniejących hooków.
Sprawdzenie wszystkich plików:

```bash
uv run --project tools --only-group pre-commit --locked pre-commit run --all-files
```

To polecenie może poprawiać pliki. Przejrzyj zmiany; hooki nie wykonują `git add`.
Hook .NET sprawdza formatowanie po restore, nie wykonuje builda i testów.

Gałąź powiązana z zadaniem używa oryginalnego identyfikatora ClickUp, np.
`CU-869fb6e1e/Autor`. Hook dodaje do commita `CU-869fb6e1e - opis`.
Bez identyfikatora ostrzega i pozostawia wiadomość; nie wymusza Conventional
Commits i nie kontaktuje się z ClickUp. Szczegóły w [instrukcji narzędzi](../tools/README.md).

## Utrzymanie instrukcji

Polecenia wykonuj z podanego katalogu i na jawnie wybranym środowisku. Nie zapisuj
wyników każdej sesji w tej instrukcji; bieżący zakres i ostatni dowód weryfikacji
należą do statusu. Jeśli procedura dotyczy tylko modułu, rozwijaj jego README,
a tutaj utrzymuj wejście i odsyłacz. Po zmianie portu lub nazwy opcji sprawdź
jednocześnie konfigurację hosta, Compose, frontend proxy i szablony Postmana.
