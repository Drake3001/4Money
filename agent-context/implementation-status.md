# Stan implementacji i kwestie otwarte

Stan implementacji ponownie sprawdzono statycznie **2026-10-29** na podstawie
rejestracji DI, repozytoriów, serwisów i kontrolerów, solucji, łańcucha migracji,
API i kontraktów workera Python oraz konfiguracji Compose i CI. Wyniki uruchomień
poniżej zachowują daty wcześniejszych weryfikacji; nie są nowym przebiegiem.
Banking i Notifications współistnieją w bieżącej gałęzi. Ostatnie migracje to
`20261026215244_AddBanking`, `20261027172612_AddNotificationsAfterRebase`
i `20261028184348_RenameReceiptS3LinkToStorageKeyAndMakeUnique`;
stara `AddNotificationRecipient` została zastąpiona, a snapshot zawiera również
`Receipt.StorageKey` i jego unikalny indeks.
To sprawdzenie dokumentacji nie uruchamia testów, migracji ani resetu bazy.
Zastosowanie aktualnego łańcucha na bazie użytkownika pozostaje do potwierdzenia.

Czytaj przed pracą nad danym obszarem; po zmianach sprawdź ponownie źródła.

Zakres wymagań ponownie porównano z `Funkcjonalności.txt`, `Baza danych.txt`,
`Architektura systemu.txt` oraz zaktualizowanymi diagramami 2026-10-27;
nie jest to nowa weryfikacja implementacji całego backendu. Aktualne oznaczenia
MVP i rozszerzenia opisuje [produkt](product.md#zakres-funkcjonalny-i-mvp).

Solucja ma obecnie jedenaście projektów testowych: po trzy User, Banking
i Notifications oraz dwa Receipt. Historyczne wyniki z poprzednich rewizji: 152 testy .NET
(User + Notifications, w tym 45 Notifications, PostgreSQL 17.6) oraz opisane
w kontekście Banking 223 testy .NET i 45 testów Pythona dla ówczesnego zakresu.
Nie sumuj tych wyników ani nie przedstawiaj ich jako przebiegu aktualnej solucji
po rebase, zmianie migracji i seeda. Aktualny zestaw wymaga ponownego uruchomienia.
CI dostarcza PostgreSQL 15 i jedną zmienną TEST_POSTGRES_CONNECTION_STRING
dla wszystkich trzech modułów. Ostatnie częściowe przebiegi opisano poniżej;
nie zastępują pełnego uruchomienia solucji z PostgreSQL.

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

Host rejestruje `IReceiptService`, klienta HTTP OCR i adapter S3. `CreateAsync`
kopiuje pojedynczy strumień do pliku tymczasowego, wykonuje OCR, upload, a następnie
zapisuje Receipt z surowym JSON-em i StorageKey. Błąd OCR przerywa proces przed
uploadem. Po błędzie zapisu bazy przesłany plik pozostaje w storage, ponieważ
niepewne potwierdzenie nie dowodzi rollbacku. Usuwanie najpierw zapisuje usunięcie
rekordu, potem podejmuje próbę usunięcia pliku z limitem 10 sekund; błąd sprzątania
jest logowany. Brak wspólnej transakcji bazy i S3 oraz automatycznego sprzątania
osieroconych obiektów. Serwis nie sprawdza właściciela paragonu; przed udostępnieniem
przez Expense/API trzeba określić i egzekwować uprawnienia.

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
użytkownika. Nie ma endpointów Receipt/Storage w backendzie; kolekcja Postman
Storage wywołuje bezpośrednio SeaweedFS S3.

Opcje OCR i S3 mają `ValidateOnStart`. `STORAGE_PUBLIC_ENDPOINT` pozwala podpisywać
URL dla klienta innym adresem niż wewnętrzny endpoint S3. Receipt.IntegrationTests
zawiera ręczny test upload → podpisany GET → odmowa anonimowego GET → delete,
włączany przez `STORAGE_INTEGRATION_TESTS=1`. Obecny CI nie uruchamia SeaweedFS,
workera ani testów pytest. PostgreSQL CI obejmuje User, Banking i Notifications.
W tej aktualizacji wykonano kontrolę statyczną, bez nowych testów aplikacji,
wywołań dostawców, migracji ani resetów.

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
| Expense | Modele i repozytoria wydatku, pozycji, kategorii i udziałów, rejestrowane w DI; osobne unikalne indeksy FK paragonu i transakcji w konfiguracji, snapshocie i migracji AddUniqueExpenseSourceReferences | Brak kontrolera i serwisów tworzenia, edycji oraz dopasowania wydatków |
| Repozytoria | Banking; Group/GroupMember; User/UserToken; Category/Expense/ExpenseItem/ExpenseShare; CategoryGroupLimit; Receipt; Notification/NotificationRecipient | BankingConnectionRepository sam zapisuje próby i połączenia; podstawowe repozytoria pozostawiają SaveChangesAsync serwisowi; warunkowe UPDATE/DELETE wykonują się od razu; brak kompletnego API wszystkich encji |
| User | Rejestracja, potwierdzenie i ponowna wysyłka e-maila; login z JWT i hashowanym refresh tokenem, rotacja refresh, logout; jedna sesja na konto i weryfikacja jej aktualności przy uwierzytelnieniu; limity IP; GET/PUT api/users/profile; zmiana e-maila z aktualnym hasłem i potwierdzeniem tokenem; forgot-password/reset-password z unieważnieniem sesji; dezaktywacja po potwierdzeniu e-mailem z usunięciem wszystkich tokenów; login nieaktywnego konta po poprawnym haśle zwraca 403; wspólny IEmailSender/SmtpEmailSender i UserEmailMessages | Brak strony potwierdzenia/resetu, zmiany hasła po podaniu aktualnego hasła; konfiguracja JWT wymaga klucza w środowisku; istnieją projekty testów jednostkowych, HTTP i PostgreSQL; brak potwierdzenia dostarczenia wiadomości do rzeczywistej skrzynki |
| Banking | Próby/połączenia, adapter Tink, callback, odczyt zgód oraz refresh on demand przez trwały worker PostgreSQL, import kont i BOOKED per rachunek; IBankingReadService oraz HTTP GET list/szczegółów rachunków i transakcji | Nowy import wymaga osobnego potwierdzenia Sandbox i zastosowania migracji AddBanking do właściwej bazy; brak odłączania banku i odnawiania zgód |
| Receipt / OCR | StorageKey z unikalnym indeksem i migracją; IReceiptService koordynuje OCR HTTP, upload S3 i zapis paragonu; odczyt rekordu, podpisanego URL i usuwanie; dwa projekty testowe | Brak podłączenia do Expense i własnych endpointów Receipt; brak potwierdzenia pełnego przepływu OCR → storage → baza w tej weryfikacji; seed zawiera syntetyczny klucz bez pliku |
| Kategoryzacja | Klasyfikacja i propozycje nazw produktów w workerze Python, taksonomia v2, flagi review, testy i narzędzia ewaluacji; model i repozytorium Category w .NET | Brak mapowania kategorii workera na encje wydatku i korekt użytkownika; historyczne eksperymenty nie potwierdzają celów jakościowych aktualnego pipeline |
| Grouping | Modele i repozytoria Group oraz GroupMember, rejestracja DI | Brak serwisów i API tworzenia/dołączania do grup, podziału kosztów i integracji Notifications |
| Limits | Model i repozytorium CategoryGroupLimit oraz pola limitów grupy | Brak serwisów i API budżetów oraz przepływu alertów |
| Notifications | Modele, AddNotificationsAfterRebase i snapshot; osobne repozytoria treści i odbiorców; NotifyAsync, GetByUserAsync, MarkAsReadAsync, MarkAllAsReadAsync; DTO; GET /api/notifications, PUT /api/notifications/{id}/read i PUT /api/notifications/read-all; Postman; trzy projekty testów | ID użytkownika dostarcza wspólny ICurrentUser; lista ma paginację i filtr isRead, stałe sortowanie od najnowszych; odczyt zachowuje pierwszą datę. NotifyAsync pomija nieaktywne konta, zwraca null bez odbiorców. Brak podłączenia Grouping/Limits i klientów. Push poza zakresem. Testy jednostkowe i HTTP przeszły częściowo zgodnie z opisem powyżej; pełny przebieg PostgreSQL po review niepotwierdzony |
| Reports | Projekt modułu | Brak usług raportowania |
| Web / mobile | Założenia w dokumentacji | Brak projektów aplikacji klienckich w repozytorium |
| Narzędzia | Hooki, testy hooków, workflow backendu z PostgreSQL i wykrywaniem UnitTests/Tests/IntegrationTests; jedenaście projektów w solucji | Od 2026-10-28 CI ustawia wspólną zmienną TEST_POSTGRES_CONNECTION_STRING dla testów bazy User, Banking i Notifications. Runner wybiera tylko śledzone projekty; „not configured” nie oznacza wykonania testów |
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
| Produkt obejmuje finanse osobiste, ale Expense wymaga grupy | Sposób reprezentowania osobistej przestrzeni wymaga ustalenia |
| ExpenseItem ma opcjonalny FkUserId, ale Funkcjonalności.txt (sekcja 3) umieszczają przypisywanie uczestników do produktów poza MVP | MVP obejmuje podział całego wydatku; obecność pola w modelu nie oznacza gotowej funkcji ani obowiązku realizacji rozszerzenia |
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
- Powiadomienia: inbox web/mobile i jego testy są gotowe w backendzie, bez infrastruktury push. Pozostaje integracja modułów źródłowych i klientów. Planowany przepływ Expense → Grouping → Notifications → User nie jest jeszcze podłączony. Od 2026-10-28 Program.cs konfiguruje wspólne scoped połączenie dla Expense, Grouping i Notifications. NotifyAsync nadal zapisuje własny kontekst; atomowość między modułami wymaga jawnego dołączenia kontekstów do transakcji i końcowego commitu przez przyszły serwis koordynujący. Koordynacja, jej testy oraz deduplikacja ponawianych wywołań nie są jeszcze zaimplementowane.

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
