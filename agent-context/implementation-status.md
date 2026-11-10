# Stan implementacji i kwestie otwarte

## Expense i integracja Receipt — stan 2026-11-10

Działa 10 operacji opisanych w [kontrakcie Expense](../notes/EXPENSE_API.md):
kategorie, dostępne transakcje bankowe, historia, szczegóły, preview tworzenia
i edycji, POST, PUT, DELETE oraz analiza i odczyt paragonu. Kontrolery korzystają
z IExpenseService, który deleguje szczegóły, historię i kategorie do
ExpenseQueryService w Services/Reads. IExpenseReadService obsługuje raporty.
ExpenseService współdzieli reguły dostępu i koordynuje przygotowanie formularza,
walidację, etykiety i zapis. Zapytania EF i operacje na zbiorach należą do
repozytoriów; SaveChangesAsync i koordynacja transakcji pozostają w serwisie.
Publiczne DTO i kontrakty serwisów mają osobne pliki.
Historię obsługuje GetHistoryAsync z ExpenseHistoryQuery, także dla wybranej grupy.
Usunięto starsze GetByGroupAsync/ExpenseQuery używane wyłącznie w teście;
kontrakt HTTP pozostaje bez zmian.
Główny przepływ Expense oddzielono od odczytów Banking i wspólnych metod zapisu
w partialach Banking i Persistence. PreparedExpenseItem przenosi ID istniejącej
pozycji; UpdateItemsAsync pracuje bez równoległej listy surowych pozycji requestu.
Walidacja procentów korzysta z jednej listy udziałów. Mapowanie bankowych DTO
jest w ExpenseResponseMapper, jak pozostałe odpowiedzi Expense.
Expense korzysta z rejestru Banking przy tworzeniu, podmianie i zwolnieniu
przypisania. Rejestr i powiązanie wydatku zapisują się w tej samej transakcji.
Lista dostępnych transakcji wyklucza przypisania Expense i GroupSettlement
przed paginacją.

Tworzenie, edycja i usuwanie aktualizują TotalPaid/TotalShare razem z Expense
w IGroupingTransactionCoordinator. Grouping dostarcza rzeczywiste członkostwa
i metadane we wszystkich środowiskach oraz ponawia walidację pod blokadą grupy.
Expense przechowuje procenty, a kwoty udziałów rozdziela metodą największych
reszt. Preview i GET nie zapisują danych. PUT/DELETE kontrolują token Version.
Host rejestruje usługi Grouping i User w sekcjach tych modułów;
AddExpenseServices rejestruje usługi Expense.

Utworzenie Expense zapisuje ExpenseAdded w tej samej transakcji przez
GroupExpenseNotificationService. Odbiorcy to wszyscy aktualni członkowie grupy
poza autorem operacji; Notifications pomija nieaktywne konta. Brak odbiorców
pozwala zatwierdzić wydatek bez powiadomienia. Edycja i usuwanie nie powiadamiają.
Istniejące powiadomienie zachowuje treść po usunięciu wydatku, a FK zeruje ExpenseId.
Bez nowych typów i bez migracji. Błąd powiadomienia wycofuje również salda,
pozycje, udziały i paragon; potwierdzony rollback sprząta nowy upload.

POST, PUT, preview, szczegóły i historia używają dat YYYY-MM-DD. Baza zachowuje
DateTime/timestamptz; nowe zapisy mają północ UTC. Początek filtra jest włącznie,
koniec wyłącznie. Migracja 20261103165622_CompleteExpenseModel obejmuje płatnika,
wersję, metadane pozycji, kategorie bez Color i udziały procentowe. Zastępuje
wcześniejsze migracje Expense tej gałęzi; Up/Down wymagają pustych wydatków,
pozycji i udziałów. Nie jest migracją przyrostową dla usuniętego łańcucha.

OCR analizuje przesłany plik bez zapisu i zwraca podpisany wynik ważny 24 h,
związany z użytkownikiem i skrótem pliku. ReceiptOcrProof wymaga poprawnego
RECEIPTOCRPROOF__SIGNINGKEY przy starcie API. POST/PUT przyjmują JSON albo
multipart z formularzem i plikiem. Upload następuje po walidacji, przed transakcją.
Receipt, powiązanie i wydatek zapisują się atomowo w bazie. GET paragonu sprawdza
dostęp i wersję Expense, po czym zwraca zapisany wynik oraz podpisany URL.
Endpoint analizy stosuje limit pięciu żądań na minutę na użytkownika
w instancji API. Nadmiarowe żądania dostają HTTP 429 bez kolejkowania.
Analiza i odczyt przekazują dla każdej proponowanej pozycji `needsReview`
oraz `reviewReason` z wyniku OCR.
Ostrzeżenia OCR zachowują `code`, `message` i `field` w obu odpowiedziach.
Nie ma osobnego uploadu ani analizy pliku już zapisanego w storage.
Po potwierdzonym rollbacku sprzątany jest nowy plik, po commicie usunięty lub
zastąpiony. Niepewny commit zachowuje plik do uzgodnienia; cleanup jest best effort.

IExpenseReadService dostarcza Reports listę, sumy, podział na kategorie i oś
czasu dzień/miesiąc/rok, z autoryzacją i filtrami. Oddziela wartość zakupów,
koszt użytkownika i kwotę zapłaconą. Daty raportów są obustronnie włączne;
historia HTTP zachowuje koniec wyłączny. Kategorie kosztu użytkownika są
proporcjonalne do jego udziału, z uzgodnieniem groszy. Szczegóły i ograniczenia:
[kontrakt odczytu](../notes/EXPENSE_REPORTS.md). Nie dodano endpointów ani migracji.

Weryfikacja odczytów dla Reports (2026-11-08): Release build całej solucji
bez błędów i ostrzeżeń, Expense.UnitTests 73/73 oraz pełne
Expense.IntegrationTests 109/109 na PostgreSQL 15, bez pominięć.
Wcześniejszy przebieg nowych ExpenseReadTests: 6/6. Potwierdzono obie daty
włącznie, dostęp historyczny, filtry/sortowanie, sumy kategorii i okresów,
rozdzielenie kosztu i kwot zapłaconych oraz grosze z ujemnymi pozycjami.
Kontrole stylu, whitespace zmienionych plików i git diff --check przeszły.
Użyto osobnego tymczasowego kontenera i losowych baz fixture; kontener usunięto.
Nie zmieniano bazy ani kontenerów aplikacji. Raporty TRX:
.cache/backend-results/expense-reports-unit-20261108/,
.cache/backend-results/expense-reports-targeted-20261108/ oraz
.cache/backend-results/expense-reports-regression-20261108/.
Pełnej regresji pozostałych modułów nie powtarzano; jej wcześniejszy wynik
dotyczy wersji z ExpenseAdded opisanej poniżej.

Pozostają: ochrona ponowień,
trwałe ponawianie cleanupu i odbudowa agregatów. Historia nadal pobiera nagłówki
i udziały osobno; zabezpieczenie jej przed mieszaniem wersji przy równoległej
edycji odłożono poza zakres porządków z tego review.

Weryfikacja po dodaniu ExpenseAdded (2026-11-08): Release build całej solucji
bez błędów i ostrzeżeń; pełna regresja 15 projektów z PostgreSQL 15:
816 zaliczonych, 0 błędów, 1 pominięty ręczny test rzeczywistego S3.
Expense.IntegrationTests 103/103, Banking.IntegrationTests 128/128,
Notifications.IntegrationTests 13/13 i User.IntegrationTests 31/31 bez pominięć.
Expense.UnitTests 66/66, Receipt.UnitTests 107/107, Shared.UnitTests 49/49,
Grouping 33/33. Testy nie wymagały poprawek kodu.
Osobny przebieg 10/10 sprawdził odbiorców ExpenseAdded, brak aktywnych odbiorców,
rollback przed i po zapisie powiadomienia, odrzucenie i anulowanie zewnętrznej
operacji oraz cleanup paragonu. Edycja i usunięcie nie tworzą nowych powiadomień;
po usunięciu ExpenseId istniejącego powiadomienia staje się null.
Kontrole stylu i whitespace zmienionych plików C# oraz git diff --check przeszły.
Testy używały osobnego tymczasowego kontenera PostgreSQL i własnych losowych
baz: SQL init, migracje oraz syntetyczny seed. Kontener testowy usunięto po
zakończeniu. Nie wykonywano migracji ani seeda na bazie aplikacji i nie zmieniano
jej kontenerów. OCR, Tink i storage w testach przepływu są zastępowane atrapami.
Raporty TRX są w ignorowanych .cache/backend-results/expense-notifications-regression-20261108/
i .cache/backend-results/expense-notifications-targeted-20261108/.

Czytaj przed pracą nad danym obszarem; po zmianach sprawdź ponownie źródła.
Aktualne oznaczenia MVP i rozszerzenia opisuje [produkt](product.md#zakres-funkcjonalny-i-mvp).
Solucja ma 15 projektów testowych: po trzy User, Banking i Notifications,
po dwa Receipt i Expense oraz Grouping i Shared. Testy PostgreSQL wymagają
TEST_POSTGRES_CONNECTION_STRING i tworzą własne losowe bazy; brak konfiguracji
lokalnie oznacza pominięcie, a w CI błąd. Starsze wyniki poniżej zachowują daty
swoich weryfikacji.

## Narzędzia migracji — weryfikacja 2026-11-07

`dotnet-tools.json` przypina `dotnet-ef` 10.0.4. Wrapper `tools/ef.cs`
ustawia projekty i kontekst migracji; `--ef-version` pozwala jednorazowo
uruchomić inną wersję bez zmiany manifestu. Nie dodano global.json ani zależności
Pythona. [Użycie i konfiguracja](development.md#modele-i-migracje).

Sprawdzono restore, kompilację wrappera na SDK 10.0.113, pomoc, wersje 10.0.4
oraz 10.0.5 i zachowanie pinu, przekazywanie argumentów i kodów zakończenia,
wywołanie z podkatalogu i checkout ze spacjami w ścieżce. Kontrola modelu
nie wykazała zmian względem snapshotu. W jednorazowym PostgreSQL 15 wykonano
SQL init i wszystkie 14 istniejących migracji; listowanie potwierdziło brak
oczekujących migracji. Operacje zapisu używały jawnego --connection do bazy
testowej; nie stosowano migracji do bazy aplikacji. Nie testowano innych SDK.

## Profile Tink z JSON — weryfikacja 2026-11-06

Nowa nakładka `tools/scripts/tink_profiles.py` obsługuje `plan`, `apply` i `resume`
z mapowaniem GUID → preset w JSON. Działa na istniejącej lokalnej bazie i tworzy
świeże tożsamości/dane Tink, zastępując wyłącznie wskazane `tink_user_id` po
weryfikacji. Nie uruchamia SQL init, migracji ani seedera. Konfigurację,
mapowanie i blokadę sesyjną wydzielono do wspólnego `tink_batch.py`; istniejący
reset zachowuje swój kontrakt i manifesty. [Instrukcja](../tools/scripts/tink_profiles/README.md).

Wykonano 73 testy Python narzędzi bez błędów i pominięć, w tym 14 nowych testów
profili. PostgreSQL 15 działał w jednorazowym kontenerze; przypadki tworzyły
własne bazy, a migracje EF wygenerowano do pliku i zastosowano wyłącznie w bazach
testów regresji resetu. Sprawdzono aktualizację wielu istniejących kont,
zachowanie pozostałych danych i niewybranych mapowań, świeże ID dla nowego
batcha, preflight wszystkich celów, konflikt unikalności, dezaktywację,
częściowy sukces, utratę checkpointu po commicie, zamrożone wejście wznowienia,
zmianę środowiska/mapowania oraz blokady i niezgodny manifest Connector.
HTTP korzystał ze stanowej atrapy Tink; rzeczywiste wywołania i uprawnienia
Sandbox pozostają niezweryfikowane. Baza aplikacji nie była modyfikowana.
Po końcowym zabezpieczeniu niepoprawnego manifestu ponownie wykonano 14/14
testów profili. Hooki higieny i `git diff --check` przeszły; kontener testowy
usunięto. Testy Python narzędzi nadal nie są częścią workflow backendu.

## Użytkownik Tink przy łączeniu banku — 2026-11-07

Usunięto osobny endpoint inicjalizacji i dedykowany serwis. Rejestracja,
potwierdzenie e-maila i login nie wywołują Tink. Rozpoczęcie połączenia zapewnia
mapowanie przez istniejący kontrakt User i adapter. Przy awarii zwraca 503
z attemptId, zapisuje nieudaną próbę best effort i pozwala ponowić rozpoczęcie
w tej samej sesji. Zachowuje poprawnie zapisane ID po błędzie tworzenia linku.
Nie ma migracji ani inicjalizacji w tle.
[Kontrakt](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#tink-user-initialization-on-connection-start--2026-11-07).

Weryfikacja 2026-11-07: build Release całej solucji bez ostrzeżeń i błędów.
Przeszło 485 testów .NET bez błędów i pominięć: Banking.UnitTests 121,
Banking.Tests 95, Banking.IntegrationTests 152, User.UnitTests 18,
User.Tests 64, User.IntegrationTests 35. Testy używały odizolowanego
PostgreSQL 15 i atrap Tink. Pełny przepływ HTTP obejmuje brak wywołań Tink
przy rejestracji/loginie, usunięcie endpointu inicjalizacji, 503 przy błędzie
upstream/sieci/timeout/odpowiedzi, brak mapowania po awarii i udane ponowienie.
Sprawdzono też zachowanie ID po awarii linku, współbieżne starty, dezaktywację
i odzyskanie po błędzie zapisu. Sprawdzono styl i whitespace zmienionych C#,
git diff --check, JSON kolekcji i składnię 136 skryptów JavaScript oraz trzy
pliki testów skryptów kolekcji (login, odczyty, refresh). Nie wykonywano wywołań
Tink Sandbox ani zmian w bazie aplikacji.

## Użytkownik Tink po rejestracji — weryfikacja 2026-11-06 (historyczne)

Na rewizji z 2026-11-06 Banking miał autoryzowany `POST /api/banking/tink-user`, wywoływany przez klienta
po rejestracji, potwierdzeniu e-maila i zalogowaniu. HTTP 204 oznacza zapisane
mapowanie, bez ujawniania ID Tink i tworzenia próby połączenia/importu.
Usługa korzysta z istniejącej blokady i adaptera; nie dodano zależności User →
Banking ani migracji. Aktualny przepływ opisuje sekcja powyżej.

Wykonano build Release całej solucji bez ostrzeżeń i błędów oraz 500 testów
bez błędów i pominięć: Banking.UnitTests 130/130, Banking.Tests 104/104,
Banking.IntegrationTests 152/152, User.UnitTests 18/18, User.Tests 64/64,
User.IntegrationTests 32/32. Testy bazy używały odizolowanego PostgreSQL 15; tymczasowy kontener usunięto
po zakończeniu testów. Nie zmieniano bazy aplikacji.
Nowe przypadki obejmują pełne HTTP register → confirm-email → login → provision
z prawdziwym JWT, błąd Tink i ponowienie w tej samej sesji, ponowne użycie
mapowania przy rozpoczęciu połączenia, współbieżną inicjalizację, wyścig z
rozpoczęciem połączenia, dezaktywację w trakcie wywołania oraz rollback zapisu
mapowania i ponowienie. Testy używają atrap Tink; istniejące testy adaptera
obejmują konflikt/lost response i odzyskanie po external_user_id. Nie wykonano
wywołań Sandbox ani nie potwierdzono uprawnień aplikacji Tink.

Sprawdzono styl i whitespace zmienionych plików C#, `git diff --check`, JSON
kolekcji i składnię 140 skryptów JavaScript. Nowe skrypty Postmana sprawdzono
lokalnie dla odpowiedzi 204 i 503; nie uruchamiano kolekcji przeciw żywemu API.

## Niezawodność połączeń Banking — 2026-11-04

Kontrolery połączeń i callbacku mapują DbException / DbUpdateException przez
lokalny filtr MVC na bezpieczne 503 BANKING_TEMPORARILY_UNAVAILABLE, bez treści
wyjątku, z attemptId: null przy braku wyniku serwisu. Callback zachowuje
Retry-After: 5 i Referrer-Policy: no-referrer. Anulowanie i błędy programistyczne
nie są przechwytywane. Wszystkie akcje kontrolera połączeń mają no-store;
odpowiedzi uwierzytelniania/routingu przed kontrolerem zachowują kontrakt hosta.

BankingConnectionRepository tworzy i zwalnia kontekst z scoped fabryki na każdą
operację, również odczyty wygaszające próby. Nie czyści trackera ani nie zapisuje
zmian scoped kontekstu wywołującego. Próba i połączenie nadal są finalizowane
atomowo we własnej transakcji. Refresh, odczyty i przypisania zachowują scoped
BankingDbContext oraz dotychczasowe granice transakcji. Bez migracji, zmian
publicznych interfejsów serwisów, automatycznego refreshu i integracji z Expense/Grouping.

Weryfikacja 2026-11-04: build całej solucji Release bez ostrzeżeń i błędów;
Banking.UnitTests 121/121, Banking.Tests 95/95, Banking.IntegrationTests 149/149
na odizolowanym PostgreSQL 15, bez pominięć. Nowe testy potwierdzają oba rodzaje
wyjątków bazy dla wszystkich endpointów połączeń i callbacku, bezpieczne treści
oraz nagłówki, propagację innych wyjątków, DI fabryki i scoped kontekstu,
zachowanie Added/Modified/Deleted i transakcji wywołującego oraz rollback po
zapisie SQL, przed commitem finalizacji. Pełny zestaw obejmuje też istniejące
testy współbieżności, replay, wygaśnięcia i własności credentials.
Sprawdzono styl i formatowanie zmienionych plików C# oraz git diff --check.
Tymczasowy kontener PostgreSQL usunięto po testach. Baza aplikacji pozostaje
bez zmian; nie wykonywano wywołań Tink.

## Serwis przypisań Banking — 2026-11-03

Dodano scoped IBankTransactionAssignmentService, implementację i repozytorium.
Serwis obsługuje odczyt snapshotu, idempotentne przypisanie oraz warunkowe zwolnienie
po typie i ID konsumenta. Zapis wymaga jawnej transakcji BankingDbContext; SQL
wykonuje się od razu, bez SaveChangesAsync, commitu lub rollbacku w serwisie.
Repozytorium blokuje BankTransaction przez FOR UPDATE i obsługuje konflikt
przypisania przez ON CONFLICT DO NOTHING. Szczegóły wyników i odpowiedzialności
wywołującego są w [kontrakcie Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#internal-assignment-service).
Nie zmieniono schematu bazy, endpointów ani połączenia BankingDbContext.
Integracja z przepływami Banking/Expense/Grouping i koordynacją transakcji
pozostaje do implementacji; serwis nie sprawdza autoryzacji ani danych konsumenta.

Weryfikacja 2026-11-03: kompilacja całej solucji bez błędów i ostrzeżeń;
Banking.UnitTests 121/121, Banking.Tests 77/77 oraz Banking.IntegrationTests
147/147 na PostgreSQL 15, bez pominięć. Nowe testy obejmują oba typy konsumenta,
idempotencję, konflikty, brak transakcji wywołującego, commit/rollback przypisania
i zwolnienia, brak zapisu/czyszczenia innych śledzonych zmian EF, a także
równoległe przypisania i przypisanie ze zwolnieniem w obu kolejnościach.
Testy współbieżności potwierdzają oczekiwanie na blokadę przez pg_stat_activity.
Sprawdzono styl i formatowanie zmienionych plików C# oraz git diff --check.
Operacje bazy wykonywano wyłącznie na tymczasowych bazach tworzonych przez testy;
baza aplikacji pozostaje bez zmian.

## Rejestr przypisań Banking — 2026-11-03

Dodano migrację `20261103203528_AddBankTransactionAssignments`, model oraz
filtry `settlementAssignment` / `expenseAssignment` istniejącej listy transakcji.
[Kontrakt Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#transaction-assignments--2026-11-03)
opisuje tabelę, niezależne flagi nullable łączone przez AND i ograniczenia etapu.
Seeder resetuje rejestr i odtwarza syntetyczne przypisania obu typów. Migracja
nie przenosi istniejących powiązań. Nie dodano zapisu rejestru w serwisach
Expense/Grouping ani API przypisań; filtr odzwierciedla wyłącznie stan rejestru.

Weryfikacja 2026-11-03: kompilacja całej solucji bez ostrzeżeń i błędów;
Banking.UnitTests 107/107 oraz Banking.Tests 77/77. Pełny przebieg
Banking.IntegrationTests na PostgreSQL 15: 137/138; starszy test cofania migracji
zakładał zachowanie wszystkich późniejszych migracji po odrzuceniu rollbacku
Expense. Po dostosowaniu asercji do etapowego rollbacku EF, ponownej kompilacji
i uruchomieniu tego testu: 1/1. Bez pominiętych testów. Testy obejmują wszystkie
kombinacje flag, paginację, izolację właściciela, ograniczenia tabeli, Up/Down/Up
i ponowne wykonanie seedera. Sprawdzono styl i formatowanie zmienionego kodu,
`git diff --check` oraz JSON i opcjonalne parametry kolekcji Postmana.
Migracje i seed wykonywano wyłącznie na osobnych bazach tworzonych przez testy;
nie zmieniano bazy aplikacji ani nie potwierdzano integracji Tink.

## Zmiany po review — 2026-10-28

- Seed dostosowano do migracji Receipt z 2026-10-28: zapisuje `storage_key`
  zamiast `s3_link` i odpowiedź OCR z `receipt`, `warnings`, `needs_review`.
  Obciążenia bankowe mają znak ujemny, wydatki pozostają dodatnie. Zachowano
  stałe ID, pięć kont i przykłady odczytu inboxa. Klucz storage jest syntetyczny,
  bez uploadu pliku. Kontrola statyczna potwierdziła zgodność kolumn 13 INSERT-ów
  ze snapshotem EF, objęcie resetem wszystkich 18 tabel modelu, zgodność pól
  JSON z modelami workera i sumę pozycji paragonu. Kompilacja API z `--no-restore`
  przeszła bez ostrzeżeń i błędów; wygenerowano SQL aktualnego łańcucha migracji.
  Nie wykonano seeda ani migracji na PostgreSQL: inicjalizację osobnego serwera
  zablokował sandbox, a zgoda na uruchomienie poza nim została odrzucona.
  Te kontrole nie potwierdzają wykonania seeda na bazie użytkownika.
- NotifyAsync zwraca Guid? i tworzy powiadomienie tylko dla aktywnych ID
  otrzymanych z IUserLookupService.GetActiveUserIdsAsync. Jedno zapytanie User
  pobiera same ID bez śledzenia encji; Notifications nie zależy od IUserRepository.
  Konta nieaktywne i nieistniejące są pomijane. Pusta lista lub brak aktywnych
  odbiorców daje null bez dodania encji i SaveChangesAsync; Guid.Empty nadal
  jest błędem walidacji. Nie powstaje puste powiadomienie bez odbiorców.
- Expense, Grouping i Notifications współdzielą scoped połączenie w Program.cs.
  Nie ma jeszcze koordynatora biznesowego ani testów wspólnej transakcji:
  samo współdzielenie połączenia nie zapewnia atomowości między modułami.
- ICurrentUser i HttpCurrentUser są w Shared/CurrentUser. Kontrolery Banking,
  User i Notifications korzystają z tego kontraktu, a nie z własnego odczytu sub.
  Stare typy IBankingCurrentUser i HttpBankingCurrentUser usunięto. Niepoprawne ID
  daje 401; dotychczasowa walidacja JWT i sesji pozostaje w User.
- Serwisy User Lookup/BankingProfile i Notifications są w osobnych folderach
  z interfejsami. Szablon API .env.example zawiera SMTP i nazwy wielkimi literami;
  lokalnego .env nie zmieniano w ramach tych prac.
- Ujednolicono konfigurację testów bazy do TEST_POSTGRES_CONNECTION_STRING,
  usuwając zmienne modułowe i fallback Notifications. Wszystkie moduły pomijają
  testy bazy bez konfiguracji lokalnie, a w CI brak zmiennej kończy się błędem.

Weryfikacja podczas implementacji tych zmian: build całej solucji bez błędów
i ostrzeżeń; testy HTTP User 64/64, Banking 66/66, Notifications 19/19 po zmianie
CurrentUser. Wcześniej przy zmianie odbiorców przeszło 17/17 testów jednostkowych
Notifications. Przebieg projektu Banking.IntegrationTests po zmianie CurrentUser
zaliczył 16 testów niewymagających bazy i pominął 28 PostgreSQL. Projekty User
i Notifications również pomijały testy bazy z powodu braku konfiguracji.
Nie potwierdzono pełnego przebiegu testów z PostgreSQL po tych zmianach.
Aktualizacja dokumentacji sama nie uruchamiała nowych testów ani operacji na bazie.

## Receipt, OCR i kategoryzacja — przegląd 2026-10-29

Aktualizacja kontraktu **2026-11-02**: host rejestruje `IReceiptService`, klienta HTTP
OCR i adapter S3. `CreateAsync` wymaga `bool runOcr`, kopiuje pojedynczy strumień
do pliku tymczasowego, opcjonalnie wykonuje OCR, następnie upload i zapis Receipt
z StorageKey. Zwraca `ReceiptCreationResult` z ID i pełnym wynikiem analizy jako
`JsonElement?`. Dla `runOcr: false` nie wywołuje OCR, a zwrócony i zapisany
`ExtractionResult` są null. `GetFileUrlAsync` generuje podpisany URL z klucza pobranego rekordu;
`GetByIdAsync` i `DeleteAsync` zachowują dotychczasowy kontrakt.
Błąd OCR przerywa proces przed
uploadem. Po błędzie zapisu bazy przesłany plik pozostaje w storage, ponieważ
niepewne potwierdzenie nie dowodzi rollbacku. Usuwanie najpierw zapisuje usunięcie
rekordu, potem podejmuje próbę usunięcia pliku z limitem 10 sekund; błąd sprzątania
jest logowany. Brak wspólnej transakcji bazy i S3 oraz automatycznego sprzątania
osieroconych obiektów. Serwis nie sprawdza właściciela paragonu; przed udostępnieniem
przez endpointy Expense trzeba określić i egzekwować uprawnienia.
Receipt zgodnie z decyzją z 2026-11-02 pozostaje modułem bez własnych endpointów;
Expense będzie korzystać z jego serwisu. Jeden wydatek może mieć najwyżej jeden
paragon. Nadpisywanie, dopisywanie i ręczna edycja danych wydatku należą do Expense.

Worker `workers/extraction_worker` ma FastAPI: `/health`, `/docs`,
`POST /api/v1/extract` i diagnostyczny `POST /api/v1/debug/extract`.
Domyślny `vision_ocr` przygotowuje obrazy, koryguje orientację, wykonuje RapidOCR
oraz analizę obrazów z OCR modelem `google/gemini-3.1-flash-lite` przez OpenRouter.
Alternatywy to `vision` i `text` (OCR, parser regułowy, klasyfikacja Gemma).
API przyjmuje 1–4 pliki jednego paragonu w polu multipart `files`, maksymalnie
cztery strony po rozwinięciu PDF/TIFF. Obsługuje także JPEG, PNG, WebP, HEIC/HEIF
oraz BMP; odrzuca animacje. Domyślne limity to 20 MiB na plik, 50 MiB na żądanie,
50 MP na stronę i 100 MP na dokument. Backendowy klient Receipt wysyła jeden plik.
Worker wykonuje pipeline kolejno i nie ma własnego uwierzytelniania; przeznaczono
go do prywatnej komunikacji z backendem.

Wynik zawiera propozycje nazw i kategorii pozycji, rabaty i kaucje, ostrzeżenia
oraz flagi ręcznej kontroli. Taksonomia v2 obejmuje 19 kategorii, w tym `zniżka`.
Są testy oraz narzędzia ewaluacji i fixture'y w `experiments/`; historyczne analizy
w `notes/ocr/` nie potwierdzają celów jakościowych dla aktualnego domyślnego modelu.
Brak mapowania tych propozycji na encje Category/ExpenseItem i przepływu korekt
użytkownika. Obsługa HTTP paragonów jest planowana przez Expense; kolekcja Postman
Storage wywołuje bezpośrednio SeaweedFS S3.

Opcje OCR i S3 mają `ValidateOnStart`. `STORAGE_PUBLIC_ENDPOINT` pozwala podpisywać
URL dla klienta innym adresem niż wewnętrzny endpoint S3. Receipt.IntegrationTests
zawiera ręczny test upload → podpisany GET → odmowa anonimowego GET → delete,
włączany przez `STORAGE_INTEGRATION_TESTS=1`. Obecny CI nie uruchamia SeaweedFS,
workera ani testów pytest. PostgreSQL CI obejmuje User, Banking i Notifications.
Przegląd z 2026-10-29 obejmował kontrolę statyczną. Weryfikacja zmian kontraktu
z **2026-11-02**: 155 testów jednostkowych Receipt zaliczone. Po uruchomieniu
Docker Desktop i SeaweedFS z lokalnego Compose ponowiono Receipt.IntegrationTests
z `STORAGE_INTEGRATION_TESTS=1`: wszystkie 5 testów zaliczone, bez pominięć.
Obejmuje to rzeczywisty upload syntetycznego obrazu, podpisany GET, odmowę
anonimowego GET i dwukrotne usunięcie. Nie potwierdza to pełnego przepływu
OCR → S3 → baza ani integracji z Expense.
Kompilacja całej solucji Release powiodła się; ostrzeżenia NuGet dotyczyły
niedostępnego źródła/audytu i zastępczych wersji pakietów testów Banking
z lokalnego cache. Kontrole stylu i formatowania zmienionych plików oraz
`git diff --check` zaliczone. Bez wywołań płatnego OCR, migracji i resetów bazy.

Źródła: [ReceiptService](../backend/ZPI4M.Backend/src/modules/ZPI4M.Receipt/Services/ReceiptService/ReceiptService.cs),
[klient OCR](../backend/ZPI4M.Backend/src/modules/ZPI4M.Receipt/Services/ReceiptAnalysisService/HttpReceiptAnalysisService.cs),
[API workera](../workers/extraction_worker/api.py), [kontrakt odpowiedzi](../workers/extraction_worker/api_models.py),
[taksonomia](../workers/extraction_worker/extraction/categories.py),
[testy S3](../backend/ZPI4M.Backend/tests/ZPI4M.Receipt.IntegrationTests/S3FileStorageIntegrationTests.cs),
[workflow](../.github/workflows/backend.yml).

## Potwierdzony stan

| Obszar | Co istnieje | Ograniczenie |
| --- | --- | --- |
| Backend | Solucja .NET 10, host API, projekty ośmiu modułów i Shared | Struktura projektu nie potwierdza realizacji MVP |
| Baza | Modele i konfiguracje Banking, Expense, Grouping, Limits, Notifications, Receipt i User; wspólny AppDbContext i migracje | Obecność migracji nie potwierdza ich zastosowania na każdej bazie |
| Expense | Historia, szczegóły, tworzenie, preview tworzenia i edycji, PUT oraz DELETE. Zmiany wydatków i sald Grouping zapisują się atomowo. | Paragon można dodać, wymienić lub usunąć przy zapisie formularza. ExpenseAdded działa przy tworzeniu. Ochrona ponowień, spójny odczyt historii i trwałe sprzątanie plików pozostają osobnymi etapami. |
| Repozytoria | Banking; Group/GroupMember; User/UserToken; Category/Expense/ExpenseItem/ExpenseShare; CategoryGroupLimit; Receipt; Notification/NotificationRecipient | BankingConnectionRepository sam zapisuje próby i połączenia; podstawowe repozytoria pozostawiają SaveChangesAsync serwisowi; warunkowe UPDATE/DELETE wykonują się od razu; brak kompletnego API wszystkich encji |
| User | Rejestracja, potwierdzenie i ponowna wysyłka e-maila; login z JWT i hashowanym refresh tokenem, rotacja refresh, logout; jedna sesja na konto i weryfikacja jej aktualności przy uwierzytelnieniu; limity IP; GET/PUT api/users/profile; zmiana e-maila z aktualnym hasłem i potwierdzeniem tokenem; forgot-password/reset-password z unieważnieniem sesji; dezaktywacja po potwierdzeniu e-mailem z usunięciem wszystkich tokenów; login nieaktywnego konta po poprawnym haśle zwraca 403; wspólny IEmailSender/SmtpEmailSender i UserEmailMessages | Brak strony potwierdzenia/resetu, zmiany hasła po podaniu aktualnego hasła; konfiguracja JWT wymaga klucza w środowisku; istnieją projekty testów jednostkowych, HTTP i PostgreSQL; brak potwierdzenia dostarczenia wiadomości do rzeczywistej skrzynki |
| Banking | Próby/połączenia, adapter Tink, callback, odczyt zgód oraz refresh on demand przez trwały worker PostgreSQL, import kont i BOOKED per rachunek; IBankingReadService oraz HTTP GET list/szczegółów rachunków i transakcji | Nowy import wymaga osobnego potwierdzenia Sandbox i zastosowania migracji AddBanking do właściwej bazy; brak odłączania banku i odnawiania zgód |
| Receipt / OCR | Analiza pliku bez zapisu, upload przy zapisie Expense, odczyt rekordu i podpisanego URL oraz usuwanie; StorageKey z unikalnym indeksem; dwa projekty testowe | Własne endpointy Receipt są celowo wykluczone; brak potwierdzenia pełnego przepływu z rzeczywistym OCR/storage w bieżącej weryfikacji; seed zawiera syntetyczny klucz bez pliku |
| Kategoryzacja | Klasyfikacja i propozycje nazw produktów w workerze Python; Expense mapuje nazwy kategorii na ID w propozycji formularza, którą użytkownik może poprawić | Worker nie zapisuje encji .NET; niejednoznaczna lub nieznana nazwa wymaga wyboru kategorii; historyczne eksperymenty nie potwierdzają jakości aktualnego pipeline |
| Grouping | Usługi i API grup, członkostw, zaproszeń, rozliczeń i sugestii, agregaty Expense i powiadomienie ExpenseAdded; szczegóły w README modułu | Odbudowa agregatów pozostaje do wykonania |
| Limits | Model i repozytorium CategoryGroupLimit oraz pola limitów grupy | Brak serwisów i API budżetów oraz przepływu alertów |
| Notifications | Modele, AddNotificationsAfterRebase i snapshot; osobne repozytoria treści i odbiorców; NotifyAsync, GetByUserAsync, MarkAsReadAsync, MarkAllAsReadAsync; DTO; GET /api/notifications, PUT /api/notifications/{id}/read i PUT /api/notifications/read-all; Postman; trzy projekty testów | ID użytkownika dostarcza wspólny ICurrentUser; lista ma paginację i filtr isRead, stałe sortowanie od najnowszych; odczyt zachowuje pierwszą datę. NotifyAsync pomija nieaktywne konta, zwraca null bez odbiorców. Grouping wysyła ExpenseAdded, GroupMemberJoined i SettlementReceived. Brak podłączenia Limits i klientów. Push poza zakresem. Testy jednostkowe, HTTP i PostgreSQL przeszły w bieżącej weryfikacji |
| Reports | Projekt modułu; Expense udostępnia IExpenseReadService z listą, agregatami kategorii i okresów | Brak implementacji raportów i endpointów w samym Reports; kontrakt Expense nie oznacza ukończenia tego modułu |
| Web / mobile | Założenia w dokumentacji | Brak projektów aplikacji klienckich w repozytorium |
| Narzędzia | Hooki, testy hooków, workflow backendu z PostgreSQL i wykrywaniem UnitTests/Tests/IntegrationTests; piętnaście projektów w solucji | CI ustawia wspólną zmienną TEST_POSTGRES_CONNECTION_STRING dla testów bazy User, Banking, Notifications i Expense. Runner wybiera tylko śledzone projekty; „not configured” nie oznacza wykonania testów |
| Infrastruktura | Wspólny Compose PostgreSQL, backendu, SeaweedFS i OCR z jednym containers/local/.env; Terraform Proxmox z workflow | Po przeniesieniu do containers/local konfiguracja Compose sprawdzona 2026-10-29 dla lokalnego .env i szablonu; containers/deploy zawiera tylko .gitkeep; bez budowania obrazów, uruchamiania usług ani migracji w tej weryfikacji; AWS pozostaje planem |

Źródła: [solucja](../backend/ZPI4M.Backend/ZPI4M.Backend.slnx),
[moduły](../backend/ZPI4M.Backend/src/modules/),
[host](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs),
[AppDbContext](../backend/ZPI4M.Backend/src/ZPI4M.Database/AppDbContext.cs),
[infrastruktura](../proxmox-infra/README.md).

Opis `UserToken` w `Baza danych.txt` jest zgodny z aktualnym kodem i snapshotem:
purpose 1–5, TargetEmail wymagany dla 1–3 i 5, NULL dla 4. Migracja
`20261023131244_AddAccountDeactivationTokens` rozszerza oba CHECK w istniejącej
tabeli; jej obecność nie dowodzi zastosowania na lokalnej bazie.
Down usuwa tokeny rodzaju 5 przed przywróceniem ograniczeń i nie reaktywuje kont.

Uzgodniony podział kontrolerów pozostaje bez zmian: UsersController obsługuje
rejestrację, potwierdzanie e-maila i sesję; profil, zmiana e-maila, reset hasła
i dezaktywacja mają osobne kontrolery. Nie planujemy ich łączenia w ramach
obecnego zakresu. Mapa kontrolerów znajduje się w architecture.md.

Źródła: [opis bazy](../../doc/Baza%20danych.txt),
[model tokenu](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/Models/UserToken.cs),
[mapowanie User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/Database/UserConfiguration.cs),
[migracja dezaktywacji](../backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/20261023131244_AddAccountDeactivationTokens.cs),
[kontrolery](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/ApiControllers/).

## Banking: jeden proces prób i połączeń

`POST /api/banking/connection-attempts` zapisuje próbę, inicjalizuje lub ponownie
wykorzystuje `User.TinkUserId`, uzyskuje delegated authorization i zwraca link.
Stary endpoint `/api/integrations/tink/link` oraz serwis generatora w hoście
zostały usunięte. Odczyty prób i połączeń działają na lokalnej bazie.

Anonimowy callback `/api/banking/tink/callback` koreluje próbę przez losowy state
(w bazie tylko skrót), weryfikuje credential w kontekście Permanent User i atomowo
zapisuje połączenie z wynikiem próby. Powtórzony callback nie tworzy duplikatu.
Połączenie ma synchronizację NOT_STARTED. Callback nie uruchamia importu;
osobny refresh opisano poniżej. Nie ma webhooków, odłączania banków ani
przedłużania zgody.

`GET /api/banking/consents` pobiera aktualne zgody przez Tink i zwraca tylko te,
które odpowiadają lokalnym połączeniom użytkownika i jego aktualnemu mapowaniu
Tink. Wymaga scope’u `provider-consents:read`. Odpowiedź zawiera lokalne ID
połączenia, status dostawcy, opcjonalny termin i możliwość przedłużenia sesji.
Nie zapisuje zgód, nie zmienia `ConsentExpiresAt` ani statusu połączenia,
nie tworzy użytkownika i nie uruchamia synchronizacji. Brak mapowania lub
połączeń daje pustą listę bez kontaktu z Tink. Callback pozostaje niezależny.

Host korzysta z rzeczywistego JWT modułu User oraz middleware uwierzytelniania
i autoryzacji. Wspólny `HttpCurrentUser` odczytuje `sub`, z obsługą `ClaimTypes.NameIdentifier`.
Nie ma zastępczego guarda dla brakującego auth. Oddzielenie zależności Banking
od User pozostaje do zaplanowania; referencję Banking do Expense usunięto.

Scalenie i weryfikacja **2026-10-26**: migracja `20261026215244_AddBanking`
obejmuje całą zmianę Banking względem
`master`: tabele prób, połączeń i synchronizacji oraz zmiany rachunków i ograniczeń
transakcji. Zastępuje dwie migracje tej gałęzi (`AddBankConnections` i
`AddBankRefreshes`), po `AddAccountDeactivationTokens`. Nie oznacza zastosowania
do bazy deweloperskiej. Publiczną dokumentację Tink
sprawdzono 2026-10-21, ale uprawnienia Client ID, dostępność rynku i rzeczywisty
przepływ Sandbox pozostają niepotwierdzone. `ContinuousAccessVerified` domyślnie
blokuje rzeczywiste operacje. Weryfikacja konfiguracji odbywa się przy użyciu,
więc brak Tink nie blokuje startu hosta i lokalnych odczytów.

Weryfikacja po scaleniu: build bez ostrzeżeń, kontrole stylu i formatowania oraz
46 testów integracyjnych Banking zaliczone bez pominięć na osobnym PostgreSQL 15.
Potwierdzono zgodność snapshotu i modelu docelowego migracji, przejście od SQL init
przez migracje, cofnięcie do ostatniej migracji z `master` oraz ponowne zastosowanie
`AddBanking`. Wszystkie sześć encji Banking ma mapowanie EF. Nie zmieniano bazy
deweloperskiej ani produkcyjnej; bazy ze starymi migracjami tej gałęzi wymagają
osobnego przejścia opisanego w development.md.

Źródła: [README Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md),
[instrukcja Tink](../notes/TINK.md),
[testy integracyjne](../backend/ZPI4M.Backend/tests/ZPI4M.Banking.IntegrationTests/).

## Refresh on demand

Implementacja z **2026-10-25**: `POST /api/banking/refreshes` zleca trwałe zadanie
użytkownika, `GET /api/banking/refreshes/{refreshId}` zwraca jego wynik i wyniki
kont. Worker w hoście API używa lease PostgreSQL, retry z Retry-After i wznawia
niezakończone konta po restarcie. Zapis jest atomowy dla rachunku; zadanie może
zakończyć się częściowym sukcesem. Pobiera dane już dostępne w Tink, również
konta Connector bez credential. Nie wywołuje aktualizacji banku ani Expense.

Migracja `AddBanking` dodaje zadania, identyfikatory Tink oraz dwa znaczniki
synchronizacji rachunku. Zastępuje unikalność numeru rachunku tożsamością Tink,
dopuszcza brak IBAN/numeru/instytucji i usuwa historyczny CHECK dodatniej kwoty
transakcji. Import zachowuje znak Tink i lokalny status `Booked`; nie zaokrągla
kwot. Pełne odkrywanie kont poprzedza oznaczanie brakujących jako IsClosed.
Pierwsze okno to 30 dni, kolejne od kursora z nakładką 7 dni; sukcesy nie są
powtarzane podczas retry. Stan BankConnection nie jest aktualizowany przez ten
proces. Starsze rachunki bez mapowania Tink pozostają bez automatycznego scalania.

Kontrakt, konfigurację i ograniczenia opisuje
[README Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand).
Kolekcja Banking zawiera folder 40 do zlecania i automatycznej walidacji wyniku.
CI przekazuje `TEST_POSTGRES_CONNECTION_STRING`, aby nie pomijać testów PostgreSQL.
Lokalna weryfikacja obejmuje adapter przez rzeczywisty HTTP do atrapy na loopback,
migrację i worker na izolowanym PostgreSQL; nie potwierdza wykonania nowego
refreshu na rzeczywistym Tink. Migracja nie pozostaje zastosowana do bazy deweloperskiej.
Weryfikacja 2026-10-25: build bez ostrzeżeń; Banking — 38 testów integracyjnych,
22 HTTP i 80 jednostkowych; User — 31 integracyjnych, 64 HTTP i 18 jednostkowych.
Wszystkie zestawy zaliczone, bez pominięć PostgreSQL. Testy User ponowiono po
rozszerzeniu resetu seed_local.sql o nowe tabele. Schemat kolekcji Postman v2.1,
składnia 24 skryptów Banking i asercje przyjęcia/statusu refreshu sprawdzone
lokalnie na danych syntetycznych; nie są dowodem runu Sandbox.

## Lokalny kontrakt odczytu Banking

Dodano i zweryfikowano **2026-10-25**: `IBankingReadService` i implementacja
scoped w hoście, ze stronicowanymi listami oraz szczegółami rachunków i transakcji.
Kontrakty w `Services/Contracts` nie ujawniają encji EF ani identyfikatorów Tink.
Każdy odczyt wymaga zaufanego lokalnego `userId`; brak użytkownika lub cudzy zasób
daje NotFound, nieaktywne konto Forbidden. Obowiązuje to również dla jawnego
filtra rachunku. Odczyt obejmuje całą historię właściciela, zamknięte rachunki,
stare mapowania i dane bez mapowania Tink. Brak zależności od klienta Tink,
uruchamiania synchronizacji, zapisów oraz nowych kontrolerów lub migracji.
Repozytoria udostępniają odczyty szczegółów ograniczone do właściciela w SQL,
a listy i nowe szczegóły używają AsNoTracking.

Weryfikacja: build bez ostrzeżeń; 277 testów .NET zaliczone bez pominięć:
Banking — 98 jednostkowych, 22 HTTP, 44 integracyjne; User — 18 jednostkowych,
64 HTTP, 31 integracyjnych. Nowe 24 przypadki obejmują 18 jednostkowych i 6
integracyjnych z odizolowanym PostgreSQL, baseline i migracjami. Potwierdzono
izolację właścicieli, mapowanie, walidację, wszystkie sortowania, stabilne strony,
filtry i zakresy domknięte, nullable, historię, zachowanie znaku i precyzji kwot,
brak śledzonych encji oraz propagację anulowania i błędów bazy.
Nie wykonywano nowych wywołań Tink ani operacji na bazie deweloperskiej.
Szczegóły: [kontrakt odczytu Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#local-account-and-transaction-reads).

## Endpointy lokalnego odczytu dla frontendu

Dodano i zweryfikowano **2026-10-25**: `BankingReadController` udostępnia cztery
GET pod `/api/banking/accounts` i `/api/banking/transactions` (listy i szczegóły).
Właściciel pochodzi z JWT, DTO HTTP są oddzielone od kontraktów serwisu,
a `amount` jest tekstem dziesiętnym z dwiema cyframi po kropce. Są filtry,
paginacja, nazwane sortowanie, walidacja 400 i bezpieczne mapowanie błędów bazy
na 503. Odczyty działają bez Tink, niczego nie zapisują ani nie synchronizują.
Postman Banking zawiera folder 80, przykłady i lokalne zmienne ID.

Weryfikacja: kompilacja bez ostrzeżeń oraz kontrole stylu i formatowania;
Banking — 98 testów jednostkowych, 60 HTTP i 46 integracyjnych zaliczone,
w tym 38 nowych HTTP i 2 nowe integracyjne. Po poprawieniu danych fixture
(potwierdzony e-mail wymagany przez produkcyjną walidację JWT) ponowiono oba
nowe testy integracyjne. Potwierdzono JWT i odrzucenie unieważnionej sesji,
izolację właścicieli, filtry, historię, nullable oraz dokładny string kwoty.
Wszystkie 73 lokalne testy skryptów kolekcji przeszły, w tym 18 nowych.
Postman CLI wykonał cztery nowe żądania na rzeczywistym lokalnym backendzie
z osobną syntetyczną bazą PostgreSQL: 14 asercji zaliczone. Generowany OpenAPI
potwierdził cztery GET oraz string `amount`. Nie wykonywano wywołań Tink ani
operacji na bazie deweloperskiej. Kontrakt i instrukcje opisują README Banking
oraz notes/TINK_POSTMAN.md.

## Automatyczne scenariusze Postmana dla refreshu

Aktualizacja **2026-10-25**: kolekcja Banking zawiera ograniczony polling, test
ponownej synchronizacji, 401/404/409, częściowy błąd, retry, pustą listę, duplikację
aktywnego zadania i wznowienie po przerwaniu procesu. Lokalna symulacja Tink,
osłona HTTPS i odrębny PostgreSQL umożliwiają uruchomienie backendu bez zmian
w jego kodzie. [Instrukcja](../postman/simulations/banking-refresh/README.md)
odróżnia dowody z API od kontroli SQL i rzeczywistego Sandboxu.

Weryfikacja: foldery 40, 50, 60, 70–76 zaliczone na rzeczywistym backendzie,
w tym przerwanie procesu SIGKILL i ponowne przejęcie tego samego zadania.
Snapshot SQL potwierdził stałe ID przy ponowieniu, cztery unikalne transakcje,
ujemne obciążenia i pominięcie operacji sprzed 45 dni. Schemat v2.1 i 128 skryptów
zweryfikowano lokalnie; 37 testów nowych skryptów i 18 testów logowania zaliczone.
Nie jest to potwierdzenie refreshu na Tink Sandbox.

Setup używa jawnego --connection do bazy testowej, ponieważ Env.Load może
nadpisać zmienną środowiskową połączenia; odmawia resetu istniejącego schematu.

## Ręczna weryfikacja Tink w Postmanie

Dodano **2026-10-22**: kolekcje Banking i Tink w [postman/](../postman/) i polską
[instrukcję zespołową](../notes/TINK_POSTMAN.md). Pokrywają backendowy flow połączenia,
odczyt zgód i błędy callbacku oraz osobną diagnostykę tokenów i Link dla
istniejącego użytkownika Tink. Osobna kolekcja User obejmuje login i pozostałe operacje konta; wspólne środowisko
przekazuje token do Banking.
Flow bankowe wymaga ręcznego kroku w przeglądarce. Same artefakty nie potwierdzają
wykonania E2E ani aktywacji produktu Sandbox.

Aktualizacja **2026-10-25**: folder 20 kolekcji Tink Diagnostics obejmuje
provider-consents, listę credentials i listę kont Data API v2, ze wspólnym
tokenem użytkownika o scope’ach tylko do odczytu. Lista kont pozwala ręcznie
pobrać kolejne strony przez `pageToken`; procedurę opisuje instrukcja Postmana.
Nie oznacza to implementacji refresh on demand ani importu w backendzie.
Folder 40 dodaje odczyt transakcji po `accountIdIn` z osobnym tokenem
`transactions:read`, kontrolą przynależności wyników do konta i ręczną
paginacją. Folder 90 sprawdza brak scope’u (403) i tokenu (401).
Użytkownik potwierdził działanie wcześniejszych odczytów credentials, zgód
i kont w Postmanie; odczyt transakcji oraz nowe próby uprawnień wymagają
osobnego runu Sandbox.

Kolekcja Banking zawiera także żądanie **10 — Zaloguj użytkownika 4Money**,
które z `userLogin` i `userPassword` uzyskuje i zapisuje wyłącznie access token
przed żądaniem 11. Osiemnaście lokalnych testów skryptu potwierdziło obsługę
sukcesu, brakujących zmiennych, błędów HTTP/JSON oraz usuwania starego tokenu.
To testy na syntetycznych odpowiedziach, bez wykonania logowania przez żywe API.

## Syntetyczne dane Connector v2

Dodano **2026-10-23**: [CLI](../tools/scripts/tink_connector.py) w projekcie uv
`tools` z presetem PLN oraz komendami `plan`, `seed`, `resume`, `verify`, `link-db`.
Skrypt uruchamia się bezpośrednio przez uv; plik `.env` wymaga jawnego `--env-file`.
CLI Google Fire obsługuje `resume <runId>` i `verify <runId>` oraz jawny `--manifest`.
`verify --hr` po udanej kontroli dopisuje czytelne podsumowanie zasobów i ID;
status operacji pochodzi z manifestu, bez dodatkowego pollingu.
Tworzy testowego użytkownika, rachunki i transakcje przez Data v2 Connector,
śledzi operację oraz kontroluje odczyt wszystkich stron. Rachunki i dostępne
salda weryfikuje przez `/data/v2/accounts`; nie wywołuje szczegółowego endpointu
sald. Manifest `accountVerification` rozróżnia sprawdzone salda i pola
niezweryfikowane (np. `creditLimit`). Manifest i logi runu
pozostają lokalnie w ignorowanym `.cache/tink-connector`; tokeny są wyłącznie
w pamięci. Nie dodano aktualizacji/usuwania danych, auth ani zmian backendu.

[Testy](../tools/scripts/tests/test_tink_connector.py) uruchamiają CLI przez
rzeczywisty HTTP do lokalnej atrapy opartej na referencji Tink. Pokrywają również
wznowienia, niepewne wyniki zapisów, błędy operacji, timeouty i zachowanie ID.
Potwierdzono wykonanie lokalnych testów, w tym brak wywołań szczegółowych sald
oraz kontrolę kwot i walut sald dostępnych na liście. Dostarczone logi rzeczywistego runu potwierdzają utworzenie użytkownika
i zapis rachunku; odczyt szczegółowych sald zakończył się 403. Weryfikacja przez samą listę
rachunków, ingest transakcji i mapowanie `reference` w Tink wymagają
ponownego uruchomienia i pozostają niepotwierdzone.
Po aktualizacji 2026-10-24 `seed --four-money-user-id GUID` opcjonalnie zapisuje
TinkUserId w lokalnym koncie po weryfikacji danych, a `link-db` odtwarza mapowanie
z ukończonego manifestu bez ponawiania zdalnych zapisów. Wymagane są istniejące,
aktywne, potwierdzone konto oraz `FOURMONEY_DATABASE_DSN`. Nowe powiązane runy
używają identyfikatora zewnętrznego zgodnego z backendem; starsze manifesty wersji 1
pozostają obsługiwane. Zapis dotyczy wyłącznie mapowania, bez tworzenia zgody,
połączeń ani importu danych do Banking. Testy z PostgreSQL i HTTP potwierdziły
konflikty, wznowienia, odtworzenie po resecie i utratę checkpointu po commit.
Ogólny seeder SQL zawiera pięć kont ze znanym hasłem testowym; samodzielny
seed_banking_user.sql został scalony i usunięty.
Aktualizacja 2026-10-25: `seed`/`resume` po weryfikacji danych oraz `link-db`
po sprawdzeniu tożsamości zastępują poprzednie `tink_user_id` wskazanego konta.
Unikalność między kontami i blokady transakcyjne pozostają zachowane.
Przeszło 47 testów CLI, w tym 11 z tymczasowymi bazami PostgreSQL i lokalną
atrapą HTTP Tink: aktualizacja, idempotencja, zachowanie poprzedniej wartości
po błędzie seedowania i aktualizacja po wznowieniu. Nie wykonano nowego runu
z rzeczywistym Tink ani zmiany mapowania w bazie deweloperskiej.
Szczegóły konfiguracji i ograniczenia: [instrukcja](../tools/scripts/tink_connector/README.md).

Dodano i zweryfikowano **2026-10-25**: zbiorczy
[`reset_state`](../tools/scripts/reset_state/README.md) wykonuje istniejący SQL
seeder, tworzy świeże runy Connector i zapisuje powiązania według mapowania JSON
(domyślnie tylko `postman.banking`). Manifest zbiorczy i blokady umożliwiają
wznowienie bez ponawiania zatwierdzonego SQL. Poprzednie zdalne zasoby pozostają.
Przeszło 59 testów narzędzi, w tym 9 nowych testów z losowymi bazami PostgreSQL,
baseline SQL i wygenerowanymi migracjami EF oraz rzeczywistym HTTP do lokalnej
atrapy Tink: pełny reset, wiele mapowań, świeże ID, rollback SQL, wznowienia,
utrata checkpointów, blokady i zgodność środowiska. Nie wykonano resetu bazy
deweloperskiej ani nowego runu z rzeczywistym Tink.

Notifications udostępnia również `MarkAllAsReadAsync` w repozytorium i serwisie
oraz `PUT /api/notifications/read-all`: oznacza wszystkie nieodczytane wpisy
odbiorcy z JWT, bez ograniczenia do bieżącej strony. Jeden warunkowy UPDATE
zachowuje wcześniejsze daty; 204 także dla pustego inboxa i przy ponowieniu.
Operację obejmują testy jednostkowe, HTTP i PostgreSQL (również równoległy
odczyt pojedynczy/zbiorczy, zachowanie wcześniejszych dat i izolacja użytkowników).

## Zatwierdzony model grup (2026-10-28)

Uzupełnienie zweryfikowane 2026-10-30: GroupMember ma `IsMember` (domyślnie
true), mapowane na `grouping.group_user.is_member boolean NOT NULL DEFAULT true`.
False oznacza opuszczenie grupy z zachowaniem wpisu. Na wyraźne życzenie
użytkownika uzupełniono istniejącą migrację ApprovedGroupingModel, designer
i snapshot; lokalną migrację cofnięto w poprzedniej wersji i zastosowano ponownie.
Odczyt schematu w kontenerze potwierdził typ, NOT NULL, default true i wpis
migracji. Wszystkie 35 testów GroupingModel przeszło na tymczasowych bazach
lokalnego PostgreSQL bez pominięć, w tym Up/Down/Up, zgodność snapshotu,
default true, zapis i aktualizacja false oraz odrzucenie NULL.
Nie dodano przepływu opuszczenia grupy ani filtrów aktywnego członkostwa
w repozytoriach. Znaczenie pola opisuje [domena](domain.md).

Wdrożono modele i mapowania Group.AdminUserId/CurrencyCode, GroupMember jako
SQL group_user z datą dołączenia i czterema agregatami, GroupSettlement,
GroupInvitation, GroupLimit oraz płatnika i kwotowe ExpenseShare. Repozytoria
odczytują administratora z Group.AdminUserId, udziały sumują/filtrują jako kwoty;
stare sortowania limitów grupy usunięto. Nowe encje mają DbSety, bez CRUD/API.
Jedna migracja ApprovedGroupingModel ma osłonę pustych danych przed DDL,
designer i snapshot. Historyczny SQL init i migracje pozostają niezmienione.
Seed zawiera komplet syntetycznych danych PLN i przelicza agregaty ze źródeł.

Nie wdrożono utrzymania/odbudowy agregatów ani serwisowych reguł członkostwa,
sumy udziałów, waluty, właściciela bankowego źródła i jego wyłączności pomiędzy
wydatkiem a rozliczeniem. Szczegóły: [domena](domain.md#przyszłe-niezmienniki-serwisów--jeszcze-niezaimplementowane).
Nowe FK są NoAction, relacje między modułami konfiguruje tylko AppDbContext.
Weryfikacja 2026-10-28: build Debug i Release bez ostrzeżeń i błędów.
Pełna regresja dziewięciu projektów: 399 testów zaliczonych bez pominięć,
z izolowanym PostgreSQL 15. Po doprecyzowaniu jawnego JoinedAt i rozszerzeniu
przypadków FK ponowiono wszystkie 34 testy modelu: zaliczone bez pominięć.
Potwierdzono SQL init → migracje → seed, Up/Down/Up, odrzucenie historycznych
danych przed DDL, ograniczenia, zapytania repozytoriów i zgodność agregatów.
Testy kontraktu potwierdziły zgodność runtime/snapshot/designer i granice modułów.
Kontrole dotnet format style, whitespace oraz git diff --check przeszły.
Nie zmieniano bazy deweloperskiej i nie wykonywano wywołań rzeczywistego Tink.

## Rozbieżności istotne podczas pracy

| Rozbieżność | Znaczenie dla agenta |
| --- | --- |
| CI wybiera tylko śledzone `.UnitTests.csproj`, `.Tests.csproj` i `.IntegrationTests.csproj` | Dodanie projektu do solucji nie zastępuje dodania plików do commita. Konfiguracja CI obejmuje już User, Banking i Notifications |
| SQL inicjalizacyjny tworzy `grouping.expense_share`; obecny model mapuje `expense.expense_share` | Zastosuj istniejącą migrację przenoszącą tabelę; nie traktuj SQL jako końcowego stanu modelu |
| Pierwsza migracja ma puste Up/Down | Zakłada bazę zainicjalizowaną SQL-em; sam `database update` nie tworzy pełnego schematu od zera |
| SQL init ma stare pola refresh tokenu, nie zawiera UserToken i używa timestampów bez strefy | Migracje AddUserTokenTable i TimestampTypeChanges rozwijają stan bazowy; nie aktualizuj historycznego SQL init przy zwykłej zmianie modelu |
| Notifications istniało w SQL init, ale brakowało go w początkowym modelu EF | AddNotificationsConfigurationClass uzupełnia snapshot i zmienia typ sent_at, zamiast ponownie tworzyć tabelę |
| Opis bazy używa `ExpenceShare` | Aktualna nazwa klasy to `ExpenseShare`; nie kopiuj literówki do nowych kontraktów |
| SQL init nie ma UNIQUE dla FK paragonu i transakcji w Expense; aktualny opis bazy, EF i snapshot już je uwzględniają | Migracja 20261021162958_AddUniqueExpenseSourceReferences tworzy dwa osobne unikalne indeksy; po jej zastosowaniu jeden paragon lub transakcja mogą należeć do najwyżej jednego wydatku |
| Nie wszystkie FK między modułami i domyślne ograniczenia z SQL init są odwzorowane w EF | Przy zmianie schematu porównuj SQL, migracje i snapshot; szczegóły opisuje domain.md |
| Snapshot deklaruje indeksy FK ExpenseItem, których nie tworzą SQL init ani stary łańcuch migracji | Historyczny brak indeksów nie wpływa na kolejność zapisu; MapExpenseShareRelationship odwzorowuje wyłącznie istniejący FK udziału do wydatku, bez zmiany indeksów |
| Expense wymaga grupy, ale grupa osobista nie jest zaimplementowana | Ustalono jednoosobową grupę; realizacja należy do Grouping |
| Historyczny SQL init i diagramy mogą nadal zawierać ExpenseItem.fk_user_id | Migracja CompleteExpenseModel usuwa pole i FK; MVP obejmuje podział całego wydatku. Schemat docelowy opisuje domena |
| Dokumentacja stosu wymienia Swagger, mobile i Python OCR | Backend .NET udostępnia OpenAPI w Development; worker FastAPI ma UI /docs i zaimplementowany OCR. Aplikacji web/mobile nadal nie ma |
| SQL init Receipt nadal używa s3_link; model i seed używają unikalnego storage_key | Migracja 20261028184348_RenameReceiptS3LinkToStorageKeyAndMakeUnique zmienia nazwę kolumny i dodaje indeks; seed wymaga jej zastosowania. Migracja nie konwertuje historycznych URL-i na klucze i wymaga braku duplikatów |
| AddNotificationsAfterRebase zastępuje starą AddNotificationRecipient; baza sprzed rebase mogła już mieć jej kolumny i tabele | Nie stosuj ponownie tych samych zmian bez sprawdzenia schematu i historii. Seed nie naprawia tego konfliktu; odtworzenie lokalnej bazy usuwa dane i wymaga zgody |
| Uzgodniono pojedynczy cel: wydatek albo grupa; NotifyAsync i aktualny seed stosują tę regułę, EF nie ma CHECK wyłączności | Walidacja serwisu nie jest ograniczeniem bazy; bezpośredni zapis SQL może nadal podać oba FK |

Źródła: [pomocnik CI](../tools/ci/backend_checks.sh),
[SQL bazowy](../database/init/02-tables.sql),
[migracje](../backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/),
[konfiguracja Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Database/ExpenseConfiguration.cs),
[model Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Models/Expense.cs),
[opis bazy](../../doc/Baza%20danych.txt), [stos](../../doc/Stos%20technologiczny.txt).

Aktualny diagram logiczny i opis bazy używają `birth_date`, zgodnie z mapowaniem
`User.BirthDate`. Dawna rozbieżność `birthday` nie występuje już w tych źródłach.

## Otwarte ustalenia

- Zakres funkcjonalny: inbox jest wprost w 2.10 (MVP), powiadomienie grupy o dodaniu wydatku w 2.7 (MVP); limity (2.6) i raportowanie (2.9) nie mają oznaczenia MVP. Automatyczne dopasowanie, zwykły push oraz interaktywny push bankowy są rozszerzeniami (sekcja 3).
- Powiadomienia: inbox i przepływ Expense → Grouping → Notifications → User obsługują ExpenseAdded przy tworzeniu. Grouping wysyła również GroupMemberJoined i SettlementReceived. Konteksty są jawnie dołączane do wspólnej transakcji koordynatora; błędy wycofują całą operację. Pozostają integracja Limits i klientów oraz ochrona ponawianych zapisów. Edycja i usuwanie Expense nie powiadamiają w bieżącym zakresie.

- Mobile: uzgodniono React + Capacitor; plik Stos technologiczny.txt nadal zawiera alternatywę React Native i wymaga ujednolicenia z tą decyzją.
- Zmiana hasła po podaniu aktualnego hasła oraz uprawnienia między modułami. Dezaktywacja jest już zaimplementowana; reaktywacji i fizycznego usuwania konta nie ma. Uwierzytelnianie używa JWT (`sub` = ID użytkownika, `sid` = ID sesji) i rotowanego refresh tokenu; szczegóły w development.md.
- Zaplanowanie kontraktu i adaptera oddzielających Banking od User; do tego czasu zachowano istniejącą implementację i mapowanie User w Banking.

- Testy User i Notifications są obecnie w solucji; używają jawnie skonfigurowanych hostów TestServer. Dane połączenia do jednorazowego serwera przekazuj przez TEST_POSTGRES_CONNECTION_STRING, zgodnie z tests/README.md.
- Konfiguracja skrzynki SMTP; adapter jest zaimplementowany, wybór serwera i faktyczne dostarczenie wiadomości nie są jeszcze potwierdzone.
- Dostawca lub symulator importu bankowego i szczegóły integracji.
- Potwierdzenie pełnej integracji istniejących klientów OCR i magazynu obrazów,
  podłączenie Receipt do Expense, mapowanie kategorii workera na encje .NET,
  uprawnienia do paragonów, obsługa korekt i pomiar jakości aktualnego pipeline.
- Reguły podziału kosztów, sald i limitów, wskazane w [domenie](domain.md#kwestie-wymagające-ustalenia-przy-zadaniu).

Nie są to automatyczne blokery wszystkich prac. Ustalaj tylko decyzje potrzebne
do konkretnego zadania. Po realizacji aktualizuj odpowiedni wiersz statusu,
źródła i datę weryfikacji; nie używaj tego pliku jako dziennika każdej sesji.

## Kontrakty HTTP Grouping — 2026-10-30

Dodano 11 szkieletowych operacji grup, członkostwa i zaproszeń oraz DTO.
Listy używają Shared.PagedResult i walidowanej paginacji. Kontrolery wymagają
uwierzytelnienia i ICurrentUser; poprawne żądania zwracają 501 ProblemDetails.
Uprawnienia administratora są docelowe, nie są jeszcze sprawdzane. Nie ma
serwisów, operacji bazodanowych, opuszczania grup ani generowania tokenów.

Weryfikacja szkieletów: build API bez ostrzeżeń i błędów; dwa testy HTTP
obejmujące 11 tras, niepoprawną tożsamość i paginację przeszły bez pominięć.
Kontrakt opisuje [README Grouping](../backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/README.md).

DTO Grouping rozdzielono na Groups/Members/Invitations, po jednym typie na plik.
Requesty list dziedziczą po Shared.PagedQuery (w tym descending=false); usunięto
duplikację walidacji paginacji w kontrolerze. Response zachowują rekordy, requesty
body są klasami z init. Kontrakt pól body i odpowiedzi pozostaje bez zmian.

## Dalsze kontrakty Grouping — 2026-10-30

Dodano szkielety CRUD rozliczeń, akceptacji zaproszenia i przekazania administratora
oraz DTO według funkcji. Requesty rozliczeń walidują pola, kwotę numeric(18,2),
metodę i różne strony; lista używa PagedQuery/PagedResult. Wszystkie poprawne
żądania nadal zwracają 501. Brak operacji bazodanowych i kontroli uprawnień
zależnych od grupy. Szczegóły kontraktu: README Grouping.

Refactor storage 2026-10-31: adapter S3 i kontrakt przeniesiono do Shared.Storage;
Receipt generuje klucze i używa IObjectStorage. Bez migracji i zmian cyklu życia.

Weryfikacja refactoru: build całej solucji bez ostrzeżeń i błędów;
Shared.UnitTests 48/48, Receipt.UnitTests 85/85. Receipt.IntegrationTests:
4 zaliczone, 1 test SeaweedFS pominięty. Pełny przebieg wykazał też
4 błędy poza storage: dwa Grouping ContractTests i dwa Banking testy
zgodności modelu/migracji. Testy PostgreSQL pominięto bez konfiguracji.
Nie potwierdzono pełnej regresji ani rzeczywistego połączenia z SeaweedFS.

## Grupy: opis, storage ikon i archiwizacja — 2026-10-31
Dodano migrację 20261031165114_AddGroupDescriptionIconsAndArchive oraz pola
Description (255 znaków), IconStorageKey i IsArchived. Serwis grup obsługuje
opis, presety z katalogu SeaweedFS, upload multipart oraz logiczne DELETE.
Archiwum jest ukrywane, dane i pliki zachowane. Shared.Storage listuje klucze
po prefiksie ze stronicowaniem. Szczegóły kontraktu i ograniczeń: README Grouping.

Weryfikacja: pełny przebieg solucji zaliczony (453 testy, 65 pominiętych).
Po dodaniu testu paginacji katalogu i limitu opisu ponowiono Shared (49/49)
i Grouping (17/17). Testy PostgreSQL i rzeczywistego SeaweedFS pominięto
bez konfiguracji. Nowe dwa testy PostgreSQL obejmują archiwizację i migrację,
ale ich wykonania nie potwierdzono; nie zweryfikowano wyścigów na żywej bazie.
Kontrola whitespace i git diff --check przeszły.
Nie stosowano migracji na bazie deweloperskiej ani nie wysyłano obrazów
do rzeczywistego SeaweedFS.

## Zaproszenia grupowe — 2026-11-01
GroupInvitationService implementuje generowanie, listę, odwołanie, podgląd
i akceptację. Token to 32 losowe bajty Base64URL; w bazie pozostaje SHA-256.
Ważność jest dowolnym przyszłym terminem. IsActive oznacza odwołanie,
nie dostępność wynikającą z czasu/licznika. Były członek jest reaktywowany
bez zmiany daty i agregatów. Obecny członek nie zużywa ponownie zaproszenia.
Mutacje blokują grupę, potem zaproszenie. Dołączenie zapisuje licznik,
członkostwo i GroupMemberJoined w transakcji wspólnej z Notifications.
Brak push i migracji. Szczegóły: README Grouping.

Weryfikacja zaproszeń: regresja solucji zaliczona, Grouping 19/19,
Shared 49/49, Receipt 85/85. Testy PostgreSQL (w tym nowy scenariusz
reaktywacji/rollbacku) i rzeczywistego S3 pominięto bez konfiguracji.
Nie potwierdzono współbieżności na żywej bazie. Bez zmian bazy deweloperskiej.

## Koordynator transakcji Grouping — 2026-11-01
Zapisy GroupService i GroupInvitationService przechodzą przez scoped
IGroupingTransactionCoordinator. Usunięto globalne ChangeTracker.Clear,
zagnieżdżone commity i sprzątanie starej ikony przed końcowym commitem.
Nie ma migracji ani zmian kontraktów HTTP.
Regresja bez konfiguracji PostgreSQL przeszła; Grouping 19/19, Shared 49/49,
Receipt 85/85. Nowe testy transakcji skompilowano. Próba wykonania siedmiu
scenariuszy na jednorazowych bazach z lokalnej konfiguracji nie dotarła do
testów: PostgreSQL localhost:5434 odrzuca połączenie. Nie potwierdzono
atomowości na żywej bazie i nie zmieniano bazy aplikacji.

### Ponowienie PostgreSQL — 2026-11-01
Po uruchomieniu kontenera PostgreSQL port localhost:5434 jest dostępny.
Na jednorazowych bazach wykonano 10 testów: GroupingTransactionTests,
InvitationFlowTests, GroupArchiveTests i InvitationQueryTests — wszystkie
zaliczone, bez pominięć. Potwierdzono wspólny commit/rollback grupy i zaproszenia,
odroczone cleanupy, odrzucenie niezarządzanej transakcji, rollback powiadomienia,
reaktywację członkostwa i migrację.
Poprawiono testowy seed (NpgsqlCommand zamiast ExecuteSqlRawAsync, które
interpretuje klamry JSON jako formatowanie) oraz asercję DbUpdateException
z wewnętrznym PostgresException. Bazy aplikacji nie zmieniano.
Nie jest to nowy pełny przebieg całej solucji z PostgreSQL.

## GroupMemberService — 2026-11-01
Wdrożono listę, szczegóły, opuszczenie i usuwanie członka przez administratora.
Repozytorium filtruje IsMember; odczyty nie śledzą encji. Grupa blokowana jest
przez GroupRepository, a zapisy korzystają z koordynatora. Bez migracji,
nowych powiadomień i zmian agregatów. Szczegóły reguł: README Grouping.
Weryfikacja: Grouping HTTP 19/19, regresja backendu bez błędów. Osobno wykonano
20 testów PostgreSQL bez pominięć (członkostwo, zaproszenia, transakcje,
archiwizacja i queries), w tym wyścig opuszczenia/przekazania administratora
i powrót po usunięciu. Baza aplikacji bez zmian. Nie oznacza to wdrożonego
utrzymania sald ani weryfikacji wszystkich możliwych wyścigów.

## Rozliczenia i serwis agregatów — 2026-11-02
Wdrożono ręczny CRUD rozliczeń i GroupMemberAggregateService obsługujący
zmianę wkładu rozliczenia oraz wydatku. Serwis agregatów wymaga aktywnej
transakcji koordynatora, nie zapisuje/commituje samodzielnie. Uzupełniono
repozytorium rozliczeń i zbiorczy odczyt członkostw. Banking, integracja
ExpenseDbContext i odbudowa agregatów pozostają poza zakresem; niepusty
BankTransactionId jest odrzucany.
Weryfikacja: build bez ostrzeżeń i błędów, Grouping HTTP 19/19, regresja
backendu zaliczona (testy wymagające osobnej konfiguracji były pomijane).
Osobno 12 nowych testów PostgreSQL zaliczonych bez pominięć: CRUD i odwracanie
wkładu, byli członkowie, cztery agregaty na kontrakcie wydatku, rollback po
błędzie źródła, walidacja i przepełnienie, odczyty bez śledzenia i równoległe
tworzenie bez utraty aktualizacji. Nie używano ExpenseDbContext w tych
scenariuszach. Bez migracji i zmian bazy aplikacji.

## Prywatność bankowego ID rozliczenia — 2026-11-02
Grouping używa istniejącego IBankingReadService do właścicielskiego mapowania
BankTransactionId. Właściciel widzi ID; pozostali członkowie i administrator
niebędący właścicielem otrzymują null. Kontrolowane niepowodzenie Banking
również ukrywa ID bez blokowania podstawowego odczytu. Nie ma nowej tabeli,
portu przypisań ani migracji. Non-null BankTransactionId na zapisie pozostaje
niedostępne. Walidacja rozróżnia pola bankTransactionId, settledAt i kwoty.
Zaktualizowano agent-context/grouping-frontedn.md według wdrożonego kodu.
Weryfikacja: Grouping 27/27, regresja backendu zaliczona (testy usług
zewnętrznych bez konfiguracji pominięte). Osobno 13/13 testów PostgreSQL:
rzeczywisty lookup Banking właściciela i administratora oraz regresja CRUD,
agregatów i współbieżności. Bazy aplikacji nie zmieniano.

## Rozdzielenie migracji Grouping/Expense — 2026-11-02
Dwie wcześniejsze migracje brancha zastąpiono:
20261102215301_AddGroupingModel (Grouping/Limits) oraz
20261102215328_AddTemporaryExpenseAggregationSupport (płatnik i kwotowe udziały).
Expense zachowuje pierwotne nazwy SQL i C#: Name, TransactionTime oraz FkPayerId.
Seed i repozytoria dostosowano; kontrakt ExpenseContribution pozostał bez zmian.
Usunięto generowane ponownie FK obecne już w SQL init. Osłony Up/Down
wymagają pustych danych odpowiednio Grouping lub Expense.

Weryfikacja: build bez ostrzeżeń/błędów, regresja całego backendu zaliczona
(testy bez konfiguracji usług zewnętrznych były pomijane). Osobno pełny projekt
Banking.IntegrationTests: 115/115 testów z PostgreSQL zaliczonych bez pominięć,
w tym Up/Down/Up, seed, agregaty, serwisy, snapshot i granice migracji.
Za zgodą użytkownika odtworzono wyłącznie lokalną bazę zpi4m_db:
SQL init → pełne migracje → seed. Odczyt historii EF potwierdził oba nowe ID,
a schemat Expense kolumny _id/name/transaction_time/fk_* i fk_payer_id.
Nie usuwano kontenera, wolumenu ani innych baz. expense.snapshot zachowano.

## Sugestie rozliczeń — 2026-11-02
Dodano GET /api/groups/{groupId}/settlements/suggestions, osobny serwis i czysty
kalkulator heurystyczny largestBalanceFirst. Zwraca salda wszystkich członków
(w tym byłych), proponowane płatności i optimalityGuaranteed=false.
Odczyt autoryzacji oraz agregatów jest jednym zapytaniem AsSingleQuery bez
śledzenia. Niespójna suma lub przekroczenie zakresu liczników daje 409.
Nie zapisuje rozliczeń ani danych. Bez migracji, Banking i odczytów Expense.
Kontrakt zaktualizowano w agent-context/grouping-frontedn.md.

Weryfikacja: Grouping 33/33, regresja backendu zaliczona (testy usług
zewnętrznych bez konfiguracji pominięte). Osobno 14/14 testów PostgreSQL
bez pominięć: sugestie i regresja rozliczeń, byli członkowie, brak zapisów,
wyzerowanie sald po zarejestrowaniu planu oraz spójny odczyt podczas zmiany
dwóch sald w innej transakcji. Build bez błędów/ostrzeżeń.
Bazy aplikacji nie zmieniano.

## Refaktor dostępu do danych Grouping — weryfikacja 2026-11-02
Usunięto nieużywany GroupingControllerBase. Serwisy grup i zaproszeń używają
repozytoriów do zapytań i operacji na zbiorach; SaveChangesAsync pozostaje
w serwisach, a zarządzanie transakcją w GroupingTransactionCoordinator.
Wspólne GroupingResult<T>/GroupingResult zastąpiły lokalne wyniki serwisów,
w tym payloady object. Kontrakt HTTP nie uległ zmianie.

Wykonano: 33/33 testy Grouping; 118/118 Banking.IntegrationTests z PostgreSQL
na losowych bazach testowych (obejmują Grouping, nowe zapytania repozytoriów,
transakcje i rollback). Regresja całej solucji bez ustawionej konfiguracji usług
zewnętrznych: 472 zaliczone, 87 pominiętych, 0 błędów. Pominięcia tej regresji
nie stanowią potwierdzenia testów integracyjnych Notifications/User/Receipt.
Osobny przebieg PostgreSQL powyżej obejmował Banking.IntegrationTests.
Nie stosowano migracji ani seeda na bazie aplikacji.

## Powiadomienia o rozliczeniach — weryfikacja 2026-11-03
Utworzenie rozliczenia w GroupSettlementService tworzy SettlementReceived (5)
wyłącznie dla ToUserId różnego od autora wpisu, również dla byłego członka.
NotifyAsync pomija nieaktywne konta bez blokowania rozliczenia. Powiadomienie
wskazuje grupę, bez ExpenseId. Edycja i usunięcie nie tworzą powiadomień.
Rozliczenie, agregaty, powiadomienie i odbiorca zapisują się w jednej transakcji
GroupingTransactionCoordinator; błędy i odmowa transakcji nadrzędnej wycofują całość.

CHECK typów rozszerzono w istniejącej 20261102215301_AddGroupingModel, wraz
z designerem, modelem późniejszego designera Expense i snapshotem. Operacje
Up/Down tymczasowej migracji Expense nie zmieniają Notifications. Down Grouping
mapuje typ 5 na General przed przywróceniem CHECK 0–4, zachowując historię.
Edycja wcześniej zastosowanej migracji nie aktualizuje bazy automatycznie.
Bazy aplikacji nie zmieniano, nie uruchamiano na niej migracji ani seeda.

Weryfikacja: Release build bez błędów i ostrzeżeń; kontrole dotnet format style
i whitespace oraz git diff --check zaliczone. Regresja solucji z PostgreSQL:
wszystkie projekty zaliczone poza jednym starym testem Notifications używającym
nieaktualnych nazw kolumn Expense. Poprawiono zapytanie testowe do bieżących
fk_expense_id/_id; ponowny przebieg Notifications.IntegrationTests: 13/13.
Łącznie końcowe wyniki projektów: 626 zaliczonych, 0 błędów, 1 pominięty ręczny
test S3. Banking.IntegrationTests: 129/129 (w tym 11 nowych przypadków
odbiorców, rollbacku i Up/Down/Up); Grouping: 33/33. Testy PostgreSQL korzystały
z własnych losowych baz. Kolekcja Notifications: poprawny JSON i składnia
6 skryptów; nie wykonywano żądań Postman do API. Dostępny Postman CLI nie
obsługuje polecenia status; nie inicjalizowano ani nie publikowano workspace.
