# Architektura systemu

Czytaj przed zmianami backendu, zależności modułów, persystencji i integracji.
To opis architektury projektowanej wraz z potwierdzonymi elementami kodu;
stopień realizacji opisuje [status](implementation-status.md).

## Modularny monolit

Backend jest jedną solucją .NET z hostem `ZPI4M.Api`, projektami modułów,
`ZPI4M.Shared` oraz `ZPI4M.Database`. Sam podział na projekty nie oznacza
osobnych usług ani niezależnych wdrożeń.

| Moduł | Odpowiedzialność | Projektowane zależności |
| --- | --- | --- |
| Banking | Integracja bankowa i pobieranie transakcji | Tylko OpenBanking |
| Receipt | Przetwarzanie obrazów paragonów | Tylko model OCR |
| Expense | Ewidencja wydatków i kategoryzacja | Banking, Receipt, User, Grouping |
| Limits | Budżety i progi wydatkowe | Expense, Grouping, Notifications, User |
| Grouping | Grupy, podział kosztów i zobowiązania | Notifications, User |
| Reports | Raporty i statystyki | Expense, Limits, Grouping, User |
| Notifications | Powiadomienia i alerty | User |
| User | Tożsamość i uprawnienia | Brak innych modułów |

Tabela opisuje projekt zależności z `../doc/Architektura systemu.txt`
i diagramu systemu; tekst i diagram ponownie sprawdzono 2026-10-27.
Aktualne źródła nie wskazują już osobnego modelu kategoryzującego jako
zależności Expense; nie oznacza to usunięcia wymagań kategoryzacji z MVP.
Nie jest to lista aktualnych
`ProjectReference`. Przed dodaniem
zależności sprawdź kod i istniejące kontrakty.
Nie wprowadzaj mikroserwisów ani nowego wzorca komunikacji bez potrzeby zadania.

### Grupa startowa nowego konta

Od migracji `20261109191600_UserImprovements` trigger PostgreSQL
`create_user_starter_group` na `"user"."user"` wywołuje funkcję
`grouping.create_user_starter_group()` po każdym INSERT użytkownika.
Funkcja tworzy zwykłą grupę „Wydatki osobiste” w PLN i aktywne członkostwo
administratora z zerowymi agregatami. Nie wywołuje serwisów Grouping ani
Notifications; kod rejestracji i zależności projektów pozostają bez zmian.
Zapis grupy i członkostwa uczestniczy w transakcji INSERT użytkownika;
błąd wycofuje całą operację. Zwykła rejestracja wysyła SMTP po SaveChanges,
przed commitem; awaria wycofuje także grupę i członkostwo. Rejestracja Google
wysyła potwierdzenie po commicie i zachowuje konto oraz grupę przy awarii SMTP.
Trigger obejmuje także seed i ręczny SQL; nie uzupełnia istniejących kont.
Zmiany tabel User/Grouping muszą uwzględniać tę funkcję. Down usuwa wyłącznie
trigger i funkcję, pozostawiając grupy i członkostwa.

Zgodnie z aktualnym opisem i diagramem Expense koordynuje dodawanie wydatku: przez
publiczny interfejs Receipt zleca odczyt paragonu, a przez interfejs Banking
pobiera transakcje do wyboru lub dopasowania. Expense odpowiada za utworzenie
wydatku i zapisanie powiązań. Receipt korzysta wyłącznie z modelu OCR,
a docelowo Banking korzysta z modułu/integracji OpenBanking. Receipt i Banking nie
zależą od Expense ani od siebie nawzajem. Są to ustalenia dotyczące współpracy
modułów i integracji. OCR działa jako osobny projekt Python poza solucją .NET,
a adapter Tink znajduje się w Banking; nie ma osobnych projektów .NET OCR/OpenBanking.

Aktualizacja zakresu z `Funkcjonalności.txt` sprawdzona 2026-10-27:
automatyczne dopasowanie paragonu do transakcji jest rozszerzeniem (sekcja 3).
Opis współpracy Expense, Receipt i Banking nie oznacza obowiązku realizacji
automatycznego dopasowania w MVP. Interaktywny push po wykryciu transakcji,
pozwalający dodać wydatek odpowiedzią „tak”, również należy do rozszerzeń.
Limity i raportowanie pozostają w zakresie funkcjonalnym bez oznaczenia MVP.
Wewnętrzny inbox jest jawnie oznaczony jako MVP (2.10); zwykły push również
znajduje się w rozszerzeniach (sekcja 3).

Uzgodniony przepływ powiadomienia o wydatku to Expense → Grouping → Notifications
→ User. Expense przekazuje dane, Grouping ustala odbiorców i wywołuje kontrakt
Notifications; Notifications nie pobiera encji Expense. Ten przepływ obsługuje
ExpenseAdded po utworzeniu wydatku. Edycja i usunięcie nie powiadamiają. Relacja FK Notification → Expense jest
konfigurowana wyłącznie we wspólnym AppDbContext; nie dodawaj referencji
projektu Expense do Notifications, bo utworzyłaby cykl zależności.

## Expense — odczyty dla Reports (2026-11-08)

Reports korzysta z IExpenseReadService i DTO w Expense.Services.Contracts.Reports.
ExpenseReadService odpowiada za autoryzację, walidację i agregaty; repozytorium
współdzieli filtrowanie i sortowanie z historią. Dane wydatku, pozycji i udziałów
raportu są pobierane jednym zapytaniem SQL bez śledzenia. ExpenseReportCalculator
korzysta z istniejącego obliczania udziałów i wartości pozycji. Nie dodano
referencji Expense → Reports, endpointów ani migracji. Reguły i przykłady:
[Expense dla Reports](../notes/EXPENSE_REPORTS.md).

## Warstwy modułu

| Warstwa | Rola i kierunek użycia |
| --- | --- |
| ApiControllers | HTTP, mapowanie odpowiedzi; korzysta z Services i DTOs |
| Services | Logika biznesowa i transformacje; korzysta z Repositories, Models i DTOs |
| Repositories | Dostęp do bazy i izolacja zapytań; korzysta z kontekstu i Models |
| Database | Kontekst persystencji i konfiguracja mapowania modeli |
| Models | Model danych; nie jest automatycznie kontraktem HTTP |
| DTOs | Dane przekazywane przez granicę API |
| Integrations, opcjonalnie | Adaptery usług zewnętrznych, np. OCR lub bankowości |

Docelowo błędy HTTP korzystają ze standardowego formatu `ProblemDetails`.
Google authentication oraz blokada zmiany e-maila konta Google w User dodają
`code` do ProblemDetails. Obecny kontrakt Banking dodaje `code` i opcjonalne
`attemptId`; ujednolicenie wymaga osobnej decyzji dotyczącej kontraktu. Kontrolery zwracają błędy biznesowe przez `Problem()`.
Walidacja DTO zwraca `ValidationProblemDetails` z błędami poszczególnych pól.
Host zapewnia `ProblemDetails` dla pustych błędów HTTP i nieobsłużonych wyjątków.

Schemat przepływu to `ApiControllers → Services → Repositories → baza`.

Integracja Google pozostaje wewnątrz User: adapter w Integrations/Google
waliduje ID token, a GoogleAuthenticationService koordynuje rejestrację
i logowanie. Wspólny IUserSessionService tworzy sesję dla Google i hasła;
nie zatwierdza transakcji wywołującego. Challenge jest jednorazowy, powiązany
z cookie HttpOnly i przechowywany w ograniczonym magazynie pamięci procesu.
Nie wymaga kolejki, zdarzeń ani nowego modułu. Ograniczenia wielu instancji,
CORS, HTTPS i kontrakt opisuje
[development](development.md#rejestracja-i-logowanie-google).
Kod używa zarówno klasycznych konstruktorów z polami private readonly,
jak i konstruktorów głównych. Oba sposoby korzystają z DI rejestrowanego
w Program.cs. Parametr konstruktora głównego nie jest publiczną właściwością;
inny moduł nie może przez niego podmienić zależności istniejącej instancji.
Różnica względem readonly dotyczy możliwości ponownego przypisania wewnątrz
klasy, nie autoryzacji ani granicy bezpieczeństwa między modułami.
Zaimplementowane repozytoria mają osobne katalogi
`Repositories/<Nazwa>Repository/` z interfejsem, implementacją i opcjonalnym
plikiem `<Nazwa>Queries.cs`. Namespace odzwierciedla katalog konkretnego
repozytorium: `ZPI4M.<Moduł>.Repositories.<Nazwa>Repository`, np.
`ZPI4M.Grouping.Repositories.GroupMemberRepository`. Ta sama przestrzeń nazw
obejmuje interfejs, implementację i Queries. UserToken, Notification oraz
NotificationRecipient nie mają Queries. W Banking katalog
`BankingConnectionRepository/` grupuje wspólną implementację oraz interfejsy
IBankConnectionRepository i IBankConnectionAttemptRepository; odświeżanie ma
osobny katalog `BankRefreshRepository/`. NotificationRecipientRepository przyjmuje
parametry pageNumber, pageSize i opcjonalny isRead; sortuje zawsze od najnowszych.
Inbox użytkownika pobiera NotificationRecipientRepository,
a NotificationRepository odpowiada za treść powiadomienia (Add/GetByIdAsync).
Od 2026-10-28 NotificationService sprawdza odbiorców przez publiczny kontrakt
User: IUserLookupService.GetActiveUserIdsAsync. UserLookupService wykonuje jedno
zapytanie AsNoTracking z projekcją samych ID istniejących, aktywnych kont.
Notifications tworzy odbiorców wyłącznie z ID zwróconych przez lookup, pomijając
nieaktywne i nieistniejące konta; nie pobiera encji User ani IUserRepository.
NotifyAsync zwraca Guid?; pusta lista wejściowa lub brak aktywnych odbiorców
oznacza null bez tworzenia encji i bez SaveChangesAsync. Guid.Empty nadal jest
błędem walidacji. Nieaktywne członkostwo nie blokuje powiadomień pozostałych osób.
NotificationRecipientRepository.MarkAsReadAsync wykonuje warunkowy UPDATE
natychmiast przez ExecuteUpdateAsync; nie wymaga SaveChangesAsync i nie
nadpisuje daty już odczytanego wpisu. Operacja ogranicza zapis do obu ID
(użytkownika i powiadomienia).
MarkAllAsReadAsync również wykonuje natychmiast ExecuteUpdateAsync, ograniczając
zapis do nieodczytanych wpisów jednego użytkownika, bez pobierania encji.
Repozytoria rejestrują dodanie i usunięcie encji, ale nie wykonują własnego
`SaveChangesAsync`. Granicę zapisu wyznacza serwis, aby np. utworzenie grupy
i dodanie administratora zapisać atomowo. Wspólny zapis wymaga tego samego
scoped kontekstu; osobne konteksty modułów nie współdzielą automatycznie
transakcji ani change trackera.

Od 2026-11-04 Banking używa rejestracji AddDbContextFactory<BankingDbContext>
z czasem życia scoped dla fabryki i kontekstu. BankingConnectionRepository tworzy
i zwalnia osobny kontekst dla każdej operacji, także odczytu wygaszającego próby.
Jego zapisy zatwierdzają własne transakcje i nie zapisują ani nie odłączają zmian
śledzonych przez wywołującego. Finalizacja próby i utworzenie połączenia pozostają
atomowe. Odczyty, refresh i rejestr przypisań zachowują dotychczasowy scoped
kontekst; przypisania nadal wymagają jawnej transakcji wywołującego.

Przegląd 2026-11-15: host przekazuje ExpenseDbContext, GroupingDbContext,
NotificationsDbContext, ReceiptDbContext i scoped BankingDbContext tę samą
scoped instancję NpgsqlConnection. Połączenie zwalnia DI, a konteksty mają
contextOwnsConnection: false. User, Limits, AppDbContext migracji i konteksty
tworzone przez fabrykę Banking zachowują osobne połączenia.
Wspólne połączenie nie rozpoczyna ani nie
współdzieli automatycznie transakcji. Serwis koordynujący rozpoczyna transakcję,
dołącza pozostałe konteksty przez UseTransactionAsync i wykonuje końcowy commit
dopiero po powodzeniu wszystkich zapisów. NotifyAsync może zachować
SaveChangesAsync; w dołączonym kontekście nie zatwierdza ono transakcji
koordynatora. Błąd musi przerwać całą operację i spowodować rollback.
Operacje na wspólnym połączeniu wykonuj kolejno, bez Task.WhenAll i bez
otwierania zagnieżdżonych transakcji. Tworzenie Expense oraz zapisy Grouping
korzystają z koordynatora; ExpenseAdded jest dołączany do tej samej transakcji.

### Expense / Grouping — integracja agregatów (2026-11-03)

ExpenseService koordynuje przygotowanie formularza przez IExpenseRequestService
i pobiera etykiety przez ExpenseQueryService. IExpenseCalculationService
wylicza dokładne kwoty udziałów metodą największych reszt (remis: UUID ordinal).
W bazie Expense nadal zapisuje procenty. Grouping otrzymuje wyłącznie neutralny
ExpenseContribution z kwotami, bez encji ani odczytu tabel Expense.

Kontrolery korzystają z IExpenseService. ExpenseService deleguje szczegóły,
historię i kategorie do ExpenseQueryService i współdzieli z nim sprawdzanie dostępu.
Główny plik ExpenseService zawiera tworzenie i preview, Editing edycję
i usuwanie, Banking dostępne transakcje, a Receipts obsługę paragonu.
Persistence zawiera wspólną obsługę zapisu i transakcji oraz aktualizację pozycji
i udziałów. Przygotowana pozycja przenosi zwalidowane ID; zapis edycji nie wiąże
jej z surowym requestem po indeksie listy. Nie zmienia to kontraktu HTTP.

Tworzenie, edycja i usuwanie używają IGroupingTransactionCoordinator jako jedynego właściciela
transakcji. EnlistAsync(DbContext) dołącza ExpenseDbContext przez UseTransactionAsync;
sprawdza wspólne scoped połączenie i odrzuca obcą transakcję. Koordynator odłącza
wszystkie dołączone konteksty po zakończeniu. EnlistNotificationsAsync deleguje do
tej samej ścieżki. Zagnieżdżona odmowa/wyjątek blokuje commit; po rollbacku zakres
DI należy zakończyć, bez globalnego ChangeTracker.Clear.

GroupExpenseContributionService wymaga aktywnego koordynatora. Pod blokadą grupy
ponownie sprawdza archiwum, walutę i aktualne członkostwo aktora/płatnika/uczestników.
Następnie wywołuje GroupMemberAggregateService i zapisuje GroupingDbContext.
Expense zapisuje wydatek/pozycje/procenty na tej samej transakcji, a koordynator
zatwierdza całość. AggregateService nadal nie wykonuje SaveChanges ani commitu.
TotalPaid rośnie o pełną kwotę płatnika, TotalShare o wyliczone udziały; liczniki
rozliczeń pozostają odpowiedzialnością istniejącego GroupSettlementService.
Błąd dowolnego zapisu/anulowanie/odmowa zewnętrznej operacji wycofuje oba moduły.
Bankowy konflikt UNIQUE nadal daje 409; niespójność/przepełnienie agregatów także 409.

Runtime używa rzeczywistych GroupMemberService i GroupExpenseMetadataLookup;
usunięto mosty i flagę UseGroupingMock. Wewnętrzny lookup metadanych jest bez
śledzenia i obejmuje archiwum po autoryzacji historycznego Expense. Expense nadal
obsługuje wyłącznie PLN. Preview i GET nie zmieniają agregatów. PUT/DELETE
odwracają poprzedni wkład i stosują nowy w tej samej transakcji co Expense.
Po zapisie nowego wydatku Expense wywołuje GroupExpenseNotificationService.
Serwis wymaga aktywnej transakcji i wcześniej autoryzowanej, zablokowanej grupy;
korzysta z istniejącego repozytorium członków, wyklucza autora operacji,
dołącza Notifications do koordynatora i wywołuje NotifyAsync z ExpenseAdded.
Nie czyta tabel Expense. Brak aktywnych odbiorców nie blokuje commitu,
a błąd wycofuje wydatek, agregaty i powiadomienie. Nie ma odbudowy danych produkcyjnych.
Seed odtwarza syntetyczne TotalShare tym samym algorytmem alokacji groszy.

Banking nie importuje Expense. Wewnętrzne zapytanie Banking stosuje warunek
obciążenia i wyklucza przypisania obu typów przed liczeniem i stronicowaniem.
Expense dołącza scoped BankingDbContext do transakcji koordynatora Grouping
przed zapisaniem lub zwolnieniem przypisania przez IBankTransactionAssignmentService.
BankingDbContext współdzieli NpgsqlConnection z koordynatorem. Konteksty
BankingConnectionRepository tworzone przez fabrykę zachowują osobne połączenia.
Rollback wycofuje przypisanie razem z wydatkiem, udziałami i agregatami.

Poniższa checklista obowiązuje także przy rozbudowie już podłączonych zapisów
Expense/Grouping i powiadomień.

### Do wykonania przy implementacji Expense i Grouping

Przy zapisie wydatku z powiadomieniem oraz operacji grupowej z powiadomieniem
(np. dołączeniu członka) obowiązuje jedna transakcja obejmująca zapis biznesowy
i powiadomienie wraz z odbiorcami. W serwisie koordynującym:

1. Korzystaj z kontekstów z tego samego zakresu DI i wspólnego połączenia.
2. Przed pierwszym zapisem wejdź do IGroupingTransactionCoordinator.ExecuteAsync;
   nie otwieraj niezależnej transakcji Expense.
3. Dołącz Expense przez EnlistAsync, a Notifications przez EnlistNotificationsAsync;
   koordynator używa UseTransactionAsync na tym samym połączeniu.
4. Zapisz dane biznesowe, a następnie wywołaj NotifyAsync. Wcześniejszy zapis
   jest potrzebny m.in. dla FK powiadomienia do nowego wydatku lub grupy.
5. Wykonaj CommitAsync wyłącznie w koordynatorze, po powodzeniu wszystkich
   kroków. Wyjątek lub anulowanie przed commitem ma wycofać całą operację;
   nie przechwytuj błędu powiadomienia po to, żeby zatwierdzić sam zapis biznesowy.

Jeśli Expense wywołuje Grouping w ramach już otwartej transakcji, Grouping
uczestniczy w tej transakcji: nie otwiera własnej i nie wykonuje końcowego commitu.
NotifyAsync zachowuje SaveChangesAsync, ale nie przejmuje własności transakcji.
Wynik null oznacza brak aktywnych odbiorców i pozwala zatwierdzić operację
biznesową bez powiadomienia. Błąd zapisu powiadomienia jest inną sytuacją
i wymaga rollbacku. Wszystkie operacje na wspólnym połączeniu wykonuj kolejno.

Dodaj testy PostgreSQL dla obu przepływów: wspólny sukces, błąd powiadomienia
po zapisie biznesowym, błąd po udanym NotifyAsync przed commitem oraz brak
aktywnych odbiorców. Po rollbacku nie mogą pozostać ani dane biznesowe,
ani powiadomienie czy przypisania odbiorców. Sam test NotifyAsync nie wystarcza.

Metody `UserTokenRepository.TryConsumeAsync` i `UserRepository.TryConfirmEmailAsync`
wykonują warunkowe DELETE/UPDATE natychmiast. Serwis potwierdzenia e-maila
obejmuje oba wywołania jedną transakcją na wspólnym `UserDbContext`, aby
zapewnić jednorazowe zużycie tokenu i atomowe potwierdzenie adresu.
Potwierdzanie i ponowna wysyłka używają `UserRepository.GetByIdForUpdateAsync`
w transakcji: parametryzowane zapytanie PostgreSQL `SELECT ... FOR UPDATE`
blokuje rekord użytkownika przed operacjami na tokenie. Dzięki wspólnej
kolejności blokad żądania potwierdzenia i ponownej wysyłki są serializowane
dla danego konta. W zwykłej rejestracji, ponownej wysyłce potwierdzenia,
zmianie e-maila, resecie lub ustawieniu pierwszego hasła i dezaktywacji
wywołania SMTP odbywają się po SaveChanges, ale przed commitem.
Zwykła rejestracja zatwierdza konto i token dopiero po przyjęciu wiadomości przez SMTP;
wymienione żądania mailowe w ten sam sposób zatwierdzają rotację tokenu.
Awaria lub anulowanie przed commitem wycofuje zapis i zachowuje poprzedni token.
Blokada użytkownika jest utrzymywana podczas sieciowej wysyłki, ograniczonej
timeoutem SMTP. Hasło, adres i aktywność konta zmienia dopiero potwierdzenie tokenu.

PostgreSQL i SMTP nie uczestniczą we wspólnej transakcji rozproszonej. Przyjęcie
przez SMTP nie gwarantuje dostarczenia do skrzynki. Jeśli SMTP przyjmie mail,
ale klient dostanie timeout albo commit bazy zawiedzie, wysłany link może być
nieważny. Użytkownik może ponowić żądanie; nie gwarantujemy wysyłki exactly-once.
Serwisy User wysyłają wiadomości bezpośrednio przez wspólny IEmailSender.
Wyjątkiem jest rejestracja Google wymagająca potwierdzenia adresu: najpierw
zatwierdza konto, grupę i token, potem wysyła SMTP. Awaria daje 503, ale zachowuje
te dane; ponowna wysyłka jest dostępna przez resend-confirmation.
W Integrations/Smtp znajdują się adapter SmtpEmailSender, konfiguracja,
UserEmailMessages.cs z tematami oraz treściami i rekordem EmailMessage
(Subject, Body) w tym samym pliku. Nie ma folderu Services/Email ani osobnych
senderów dla poszczególnych rodzajów wiadomości.
Obsługa błędu wysyłki pozostaje w serwisie danego przepływu, ponieważ reset hasła
zachowuje inną odpowiedź niż pozostałe operacje.
Potwierdzenie dezaktywacji używa tej samej kolejności blokad i transakcji:
jednorazowo zużywa token, ustawia IsActive = false i usuwa wszystkie tokeny konta.
Każda walidacja JWT sprawdza istnienie sesji i aktywność konta w bazie,
więc dezaktywacja odrzuca też wcześniej wydane JWT przy kolejnych żądaniach.
Kontrakty Services i DTOs mogą istnieć przed implementacją pełnego przepływu.
Wydzielanie Integrations jest możliwością projektową, nie wymogiem tworzenia
pustych katalogów w każdym module.

Serwisy Banking są pogrupowane według funkcjonalności: `Services/Reads` zawiera
`BankingReadService` i jego interfejs, `Services/Connections` — serwis połączeń,
interfejsy połączeń i callbacku oraz opcje, a `Services/Refresh` — synchronizację.
Namespace’y odpowiadają katalogom. Wspólne kontrakty pozostają w
`Services/Contracts`. Bieżącą tożsamość aplikacji udostępnia wspólny ICurrentUser.

Od 2026-10-28 pary interfejs/implementacja UserLookupService i
UserBankingProfileService znajdują się odpowiednio w User `Services/Lookup`
i `Services/BankingProfile`. NotificationService i INotificationService są
w Notifications `Services/Notifications`. Namespace’y odpowiadają katalogom.
Szablon API `.env.example` zawiera poświadczenia, także SMTP, wymagany
`FRONTEND_URL` oraz opcjonalne ustawienia Google i prefiksów storage. Niesekretne
wartości domyślne znajdują się w `appsettings.json`, a zmienne środowiskowe
je nadpisują. Lokalny `.env` nie jest szablonem do kopiowania sekretów do repozytorium.

## Obecna integracja Receipt i workera OCR

Receipt udostępnia `IReceiptService` i `IReceiptAnalysisService` (HTTP).
Adapter magazynu plików `IObjectStorage` znajduje się w `ZPI4M.Shared.Storage`.
Kontroler paragonów deleguje do `IExpenseService`. Analiza przesłanego pliku
wywołuje worker bez zapisu, a Expense zwraca podpisany oryginalny wynik
związany z użytkownikiem i skrótem pliku. Zapis Expense weryfikuje proof,
przechowuje oryginalny JSON w Receipt i zapisuje zatwierdzony formularz.

`ReceiptDbContext` współdzieli połączenie z Expense i Grouping. Koordynator
dołącza go jawnie przy zapisie lub usuwaniu rekordu Receipt. Upload pliku wykonuje się po walidacji, przed rozpoczęciem transakcji zapisu.
Plik pozostaje poza transakcją PostgreSQL. Callback po potwierdzonym rollbacku usuwa nowy plik,
a callback po commicie usuwa plik zastąpionego lub odpiętego paragonu. Dostęp do
zapisanego paragonu jest sprawdzany przez Expense.

Worker FastAPI w `workers/extraction_worker` ma osobne zależności Python/uv
i udostępnia `POST /api/v1/extract`. Domyślny pipeline łączy RapidOCR z analizą
obrazów i OCR przez Gemini w OpenRouter. Odpowiedź obejmuje propozycje kategorii
produktów; nie powstają z niej automatycznie encje ExpenseItem ani Category.
Worker nie ma własnego auth, działa w prywatnej sieci i wykonuje pipeline kolejno.
Opis API, wariantów pipeline i limitów ma główne miejsce w [README workera](../workers/extraction_worker/README.md).

Klient .NET wysyła pojedynczy plik jako multipart `files` i sprawdza strukturę
`receipt` / `warnings` / `needs_review`. Adapter S3 zapisuje prywatne obiekty,
a dostęp udostępnia przez podpisany GET. `STORAGE_ENDPOINT` służy operacjom
backendu, `STORAGE_PUBLIC_ENDPOINT` opcjonalnie adresowi podpisanych URL-ów dla
klienta. W Compose są to odpowiednio `http://seaweedfs:8333` i domyślnie
`http://localhost:8333`. Obie integracje mają walidację opcji przy starcie hosta.

## Obecny podział kontrolerów User

Pozostawiamy uzgodniony podział kontrolerów. Ogólna nazwa UsersController
nie oznacza, że należy przenosić do niego pozostałe funkcjonalności.

| Kontroler | Operacje pod `/api/users` |
| --- | --- |
| UsersController | register, confirm-email, resend-confirmation, login, refresh, logout |
| UserProfileController | GET/PUT profile |
| EmailChangeController | POST profile/change-email, confirm-email-change |
| PasswordResetController | POST forgot-password, reset-password |
| AccountDeactivationController | POST profile/deactivate, confirm-deactivation |

Kontrolery współdzielą prefiks tras; nazwy klas nie wyznaczają adresów,
ponieważ routing jest jawny. Nie ma osobnych AuthenticationController
ani RegistrationController. Szczegóły autoryzacji i odpowiedzi opisuje
[instrukcja pracy](development.md).

## Proces Banking i granica Tink

Stan po rebase sprawdzono 2026-10-24. Zależność `Expense → Banking` pozostaje.
Banking nie zależy od Expense; nieużywaną referencję usunięto, aby uniknąć cyklu.
Obecna zależność `Banking → User` jest tymczasowym odstępstwem od docelowego
podziału z tabeli. Oddzielenie przez kontrakt i adapter wymaga osobnego planu;
nie zostało wykonane w ramach rebase’u.

`BankingConnectionService` koordynuje repozytoria prób/połączeń i adapter
`ITinkConnectionGateway`. Banking zależy także od publicznego serwisu profilu
modułu User: odczytuje aktywność i inicjalizuje istniejące `User.TinkUserId`.
User nie zależy od Banking ani Tink. Inicjalizacja mapowania jest serializowana
transakcyjną blokadą doradczą PostgreSQL, niezależną od zapisów logowania i profilu;
wywołanie dostawcy jest przekazywane jako operacja.
Konfiguracja Banking wykorzystuje istniejące mapowanie User dla kluczy obcych.

Od 2026-10-28 ICurrentUser i HttpCurrentUser znajdują się w
`ZPI4M.Shared/CurrentUser` (namespace ZPI4M.Shared.CurrentUser). Zastępują
IBankingCurrentUser i adapter z hosta API. Implementacja używa IHttpContextAccessor,
uwierzytelnionej tożsamości oraz claimu sub z fallbackiem NameIdentifier;
niepoprawny lub pusty GUID daje null. Nie dotyczy to TinkUserId.
Banking, User i Notifications korzystają ze wspólnego kontraktu w kontrolerach,
zamiast bezpośredniego parsowania sub; brak ID daje 401. Host i hosty testowe
rejestrują scoped ICurrentUser/HttpCurrentUser oraz IHttpContextAccessor.
Shared ma FrameworkReference Microsoft.AspNetCore.App, ponieważ na życzenie
projektu zawiera także implementację HTTP. Walidacja JWT i odczyt identyfikatora
sesji sid pozostają osobną odpowiedzialnością warstwy uwierzytelniania/User.

Host rejestruje zależności i wspólną implementację tożsamości. Koordynacja procesu
nie znajduje się już w serwisie generatora linku hosta. Tink odpowiada za tokeny,
Permanent Users, link i weryfikację credential; Banking za trwałe próby i stan
lokalny. Szczegóły opisuje
[README Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md).

## Persystencja i migracje

PostgreSQL przechowuje dane w schematach modułowych. Moduły mają własne
konteksty, a konfiguracje `*Configuration.Configure` są wykorzystywane także
przez wspólny `AppDbContext` w projekcie `ZPI4M.Database`.
Migracje całego modelu znajdują się w tym projekcie; tabela historii migracji
jest skonfigurowana w schemacie `public`.

Relacje między modułami, Notification → Expense i Expense.FkPayerId → User,
są konfigurowane we wspólnym AppDbContext; FK płatnika używa NoAction.

Nazwy tabel i kolumn są mapowane jawnie, zwykle w snake_case. Przy zmianie
modelu sprawdź konfigurację, kontekst modułu i wspólny kontekst migracji.
Nie zakładaj, że sama zmiana właściwości C# aktualizuje istniejącą bazę.

SQL w `database/init` tworzy bazową strukturę przy pierwszej inicjalizacji
kontenera. Historia migracji rozwija ten stan. Procedura i ograniczenia
znajdują się w [instrukcji pracy](development.md#modele-i-migracje).

## Frontend i granica przeglądarki — przegląd 2026-11-15

`App.tsx` składa UserProvider → Provider → GroupsProvider → ExpensesProvider.
Nawigacja widoków jest stanem React; specjalne wejścia z linków e-mail
i zaproszeń interpretuje `emailLinks.ts`. `src/api/client.ts` obsługuje Bearer,
rotację refresh tokenu współdzieloną przez równoległe żądania 401 i jednorazowe
ponowienie. Sesja trafia do localStorage; ID token Google pozostaje w pamięci
komponentu. `src/mocks/` zawiera typy, walidację i formatowanie, a nie symulację API.
HTTP 501 otwiera wspólny dialog niedostępności; błąd sieci ma kod klienta 0.

Widoki obejmują konto, grupy/zaproszenia/rozliczenia, Expense z OCR, Banking
i inbox. Pulpit pokazuje odczyty bankowe i ostatnie powiadomienia; nie jest
implementacją Reports ani Limits. Biblioteka wykresów w zależnościach nie
potwierdza istnienia raportów. Wspólny Modal korzysta z portalu do document.body.

Łączenie banku otwiera nową kartę z URL Tink. Główna karta odpytuje próbę
co 3 sekundy, a aktywny refresh co 2,5 sekundy. Zapamiętywane są ID ostatniej
próby i refreshu, nie pełna historia zadań. Callback API przy Accept: text/html
zwraca samodzielną stronę wyniku bez przekierowania do kolejnej sesji frontendu;
użytkownik wraca do pierwotnej karty. Dla klienta JSON zachowuje DTO/ProblemDetails.
Obsługuje to BankingBrowserResponse oraz filtr błędów bazy dla callbacku,
z zachowaniem statusów HTTP, no-store i ograniczeń CSP/referrer.

Uzupełnienie przeglądu frontendu 2026-11-17: obecność źródła bankowego Expense
wynika z `bankTransactionId`, niezależnie od dostępności DTO transakcji. Dla
innego płatnika formularz i szczegóły nie pobierają prywatnych danych Banking;
pokazują informację o powiązaniu i właścicielu. Formularz zachowuje kwotę Expense
na potrzeby preview/zapisu oraz OCR, blokuje płatnika i kwotę, ukrywa zmianę
i odłączenie transakcji. Są to ograniczenia UI; nie zastępują autoryzacji API.

Provider w `store.tsx` automatycznie pobiera connections, consents i accounts
po zalogowaniu; błąd dowolnego żądania w `Promise.all` ustawia wspólny błąd
bankowości. BankingPage scala identyczne komunikaty błędów żądań i udostępnia je
podwidokom przez lokalny kontekst, aby nie powielać alertów. Wyniki refreshu
mapuje osobno `refreshErrorText` w `mocks/bank.ts`. Interpretację kodu
`BANKING_TINK_UNAVAILABLE` opisuje [diagnostyka](development.md#diagnostyka-błędów-banking--2026-11-17).

Linki e-mail User są niezależnym przepływem: wymagany `FRONTEND_URL` w hoście
zasila UserEmailLinkOptions i podlega walidacji przy starcie. Logo maili jest
osadzonym zasobem MIME CID, a daty ważności są formatowane w Europe/Warsaw.
Konfigurację i polecenia opisuje [development](development.md).

## Stos obecny i docelowy

| Obszar | Stan potwierdzony / założenie |
| --- | --- |
| Backend | C#, .NET 10, ASP.NET Core, EF Core, Npgsql |
| API | Obecnie OpenAPI w Development; Swagger i Postman wymieniono w planie narzędzi |
| Baza lokalna | PostgreSQL 15 Alpine w Docker Compose |
| Kontenery aplikacji | Jeden containers/local/docker-compose.yml: frontend Vite, PostgreSQL, backend .NET, SeaweedFS 4.48 i worker OCR; jedna konfiguracja containers/local/.env; wewnętrzna komunikacja po nazwach usług |
| Web | React + TypeScript + Vite i Tailwind; ekrany i klient API z JWT. Przeglądarka używa /api przez proxy Vite dev/preview do API_PROXY_TARGET (lokalnie localhost:5283, Compose backend:8080). Kontener developerski Node 22, bind mount kodu, osobny wolumen node_modules i HMR z opcjonalnym pollingiem; bez obrazu produkcyjnego. Uruchomienie: frontend/README.md i containers/local/README.md; ograniczenia: implementation-status.md |
| Mobile | Uzgodniono React + Capacitor; Stos technologiczny.txt nadal wymienia alternatywę React Native, brak aplikacji w repo |
| OCR i storage | Worker FastAPI z OCR i kategoryzacją produktów; Receipt opcjonalnie wywołuje POST /api/v1/extract i korzysta z magazynu zgodnego z S3; HTTP analizy, zapisu i odczytu paragonu jest dostępne przez Expense; brak nowego potwierdzenia pełnego przepływu z rzeczywistymi usługami |
| Automatyzacja | GitHub Actions, pre-commit, narzędzia Python zarządzane przez uv |
| Testy backendu | Piętnaście projektów: po trzy User, Banking i Notifications, dwa Receipt i dwa Expense (UnitTests, IntegrationTests), Grouping i Shared; CI dostarcza PostgreSQL 15 dla testów bazy |
| Infrastruktura | Obecna konfiguracja Terraform dla Proxmox; Linux i Tailscale w środowisku developerskim |
| Prezentacja | AWS jako założenie docelowe, nie potwierdzone wdrożenie aplikacji |

Banking zawiera kontrolery, serwisy, adapter Tink, odczyt zgód i worker importu
rachunków oraz transakcji na żądanie. Publiczny `IBankingReadService` udostępnia
lokalne listy i szczegóły rachunków/transakcji przez DI, z wymaganym właścicielem
i osobnymi kontraktami danych w `Services/Contracts`. Odczyt nie wymaga Tink,
nie uruchamia synchronizacji. Expense może użyć
tego interfejsu do wyboru transakcji i utrzymywania przypisania; tworzenie wydatku pozostaje
w Expense. `BankingReadController` udostępnia listy i szczegóły rachunków oraz
transakcji frontendowi przez cztery endpointy GET, z właścicielem z JWT, osobnymi
DTO HTTP i kwotami jako tekst dziesiętny zachowujący dokładność.
Szczegóły kontraktu opisuje
[README Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#local-account-and-transaction-reads).
Implementacja nie potwierdza rzeczywistego przepływu Sandbox.

## Źródła

- [Opis architektury](../../doc/Architektura%20systemu.txt)
- [Diagram systemu](../../doc/Architektura%20systemu.png) i [warstw modułu](../../doc/Architektura%20modułu.png)
- [Plan stosu technologicznego](../../doc/Stos%20technologiczny.txt)
- [Host API](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs)
- [Wspólny kontekst migracji](../backend/ZPI4M.Backend/src/ZPI4M.Database/AppDbContext.cs)
- [Compose](../containers/local/docker-compose.yml)
- [Infrastruktura Proxmox i jej proces wdrożenia](../proxmox-infra/README.md)

## Worker synchronizacji Banking

Refresh on demand pozostaje częścią modularnego monolitu. BackgroundService
w hoście API przetwarza trwałe zadania PostgreSQL; kontroler tylko zleca zadanie
i udostępnia jego wynik. Serwis/processor koordynuje adapter Tink i repozytorium,
które przejmuje zadania przez lease oraz zapisuje dane atomowo dla rachunku.
Nie ma zewnętrznej kolejki. [Kontrakt i parametry workera](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand).

## Model grup po zatwierdzonym planie — stan 2026-11-10

Grouping posiada Group, GroupMember (SQL group_user), GroupSettlement i
GroupInvitation; Limits posiada GroupLimit obok niezmienionego CategoryGroupLimit.
Expense posiada płatnika i udziały procentowe (stan po rebase 2026-11-03). Mapowania wewnątrz modułów są w ich
konfiguracjach; nowe relacje między modułami są wyłącznie w AppDbContext i mają
DeleteBehavior.NoAction. Szczegóły pól opisuje
[domena](domain.md#zatwierdzony-model-grup--2026-10-28).
Serwisy Grouping i Expense zapisują źródła i agregaty atomowo przez koordynator,
sprawdzają członkostwo, udziały i walutę. Expense weryfikuje właściciela źródła
bankowego i utrzymuje rejestr jego przypisania we wspólnej transakcji.
Grouping nadal odrzuca nowe bankowe powiązania rozliczeń. Odbudowa agregatów
pozostaje do wykonania. Bieżące API i ograniczenia opisuje
[status](implementation-status.md).

Storage jest współdzielony w ZPI4M.Shared.Storage: IObjectStorage, S3ObjectStorage,
FileUrlResult i konfiguracja STORAGE_*. Klucze generuje moduł wywołujący;
Receipt używa RECEIPT_STORAGE_PREFIX (domyślnie receipts/). Testy adaptera:
ZPI4M.Shared.UnitTests.

Od 2026-11-12 normalizacja ikon User i Grouping jest wspólna w
`ZPI4M.Shared.Images.IconImageProcessor` (Magick.NET). Pomocnik dekoduje i skaluje
obraz do WebP; nie zna użytkowników, grup, kluczy storage ani transakcji.
Moduły mapują jego błędy i wykonują upload/sprzątanie we własnych serwisach.
Frontend ma analogiczny wspólny pomocnik `src/shared/iconImage.ts` dla lokalnej
konwersji HEIC/HEIF i zmniejszenia przed wysłaniem. Kontrakt limitów:
[development](development.md#zdjęcia-profilu-i-prefiksy-storage).

UserIconService korzysta z tego samego IObjectStorage bez zależności od Grouping.
Wybór presetu i upload są częścią aktualizacji profilu, z blokadą rekordu konta
w UserDbContext. Nowy plik jest sprzątany po potwierdzonym rollbacku, stary po
commit; niepewny commit zachowuje pliki. Nie stosujemy koordynatora Grouping
ani wspólnej transakcji bazy i S3. Szczegóły i prefiksy dla User/Grouping/Receipt:
[development](development.md#zdjęcia-profilu-i-prefiksy-storage).

## Grupy: opis, obrazy i archiwizacja — 2026-10-31
Nowa migracja AddGroupDescriptionIconsAndArchive dodaje description varchar(255)
nullable, icon_storage_key nullable oraz is_archived NOT NULL DEFAULT false.
Icon i IconStorageKey są rozłączne. Presety pochodzą z listowania storage pod
GROUP_ICON_PRESET_PREFIX (domyślnie icons/presets/groups/), bez listy ID w kodzie.
GroupIconService korzysta z Shared.Storage; moduł generuje klucze własnych plików.
POST/PUT grup obsługują JSON i multipart. Brak instrukcji ikony na PUT ją zachowuje;
removeIcon usuwa wybór. Opis jest przycinany, pusty staje się null, limit wynosi
255 znaków Unicode. DELETE archiwizuje, zachowując dane i pliki; odczyty ukrywają
archiwum. Szczegóły i ograniczenia: README modułu Grouping.
Migracja nie została zastosowana na bazie deweloperskiej.

## Zaproszenia grupowe — 2026-11-01
GroupInvitationService implementuje generowanie, listę, odwołanie, podgląd
i akceptację. Token to 32 losowe bajty Base64URL; w bazie pozostaje SHA-256.
Ważność jest dowolnym przyszłym terminem. IsActive oznacza odwołanie,
nie dostępność wynikającą z czasu/licznika. Były członek jest reaktywowany
bez zmiany daty i agregatów. Obecny członek nie zużywa ponownie zaproszenia.
Mutacje blokują grupę, potem zaproszenie. Dołączenie zapisuje licznik,
członkostwo i GroupMemberJoined w transakcji wspólnej z Notifications.
Brak push i migracji. Szczegóły: README Grouping.

Repozytorium zaproszeń nie udostępnia IQueryable: GroupInvitationQuery w
GroupInvitationQueries.cs opisuje filtr IsActive i wspólną paginację.
GetPageAsync wykonuje filtrowanie i sortowanie w repozytorium, a
GetByTokenHashAsync zapewnia odczyt bez śledzenia. Serwis mapuje wynik na DTO.

## Koordynacja zapisów Grouping — 2026-11-01
Wszystkie operacje zapisu Grouping korzystają ze scoped
IGroupingTransactionCoordinator. Zewnętrzny przepływ otacza operacje przez
ExecuteAsync(action, succeeded, cancellationToken); wywołania zagnieżdżone
współdzielą transakcję i nie zatwierdzają jej samodzielnie. succeeded dla
wyników serwisów oznacza status 2xx. Odmowa lub wyjątek zagnieżdżony blokuje
końcowy commit nawet po przechwyceniu błędu. Walidacyjne odmowy przed zapisem
są zgłaszane do koordynatora przez Reject.

Nie rozpoczynaj bezpośrednio transakcji EF przed wejściem do koordynatora:
taka transakcja jest odrzucana przed mutacją. SaveChangesAsync nadal zapisuje
wszystkie oczekujące zmiany kontekstu jako jedną jednostkę pracy. Operacje
na wspólnym połączeniu wykonuj kolejno, bez równoległego używania zakresu DI.

Po rollbacku zakończ użycie zakresu DI i utwórz nowe konteksty. Koordynator
odrzuca kolejne zapisy w zakończonej niepowodzeniem jednostce pracy.
Nie czyści globalnie ChangeTracker ani nie naprawia stanu encji w pamięci.
Notifications jest dołączany jawnie do tej samej transakcji i połączenia;
właściciel transakcji odpowiada za odłączenie.

AfterCommit służy usunięciu zastąpionych własnych ikon dopiero po końcowym
commicie. AfterRollback sprząta nowe uploady tylko po potwierdzonym rollbacku.
Przy niepewnym wyniku commitu zachowujemy pliki. Cleanup jest best effort,
nie zmienia potwierdzonego sukcesu. Brak trwałej kolejki/outboxa.

## Członkostwo grup — 2026-11-01
GroupMemberService obsługuje listę, szczegóły, opuszczenie i usunięcie członka.
Listy obsługują active/former/all (domyślnie active), paginację i kierunek
sortowania po UserId. Odczyty dostępne aktywnym członkom niearchiwalnej grupy.
Szczegóły mogą dotyczyć także byłego członka, ale były członek nie odczytuje grupy.
Leave/Remove zmieniają tylko IsMember=false; historia, JoinedAt i wszystkie
agregaty pozostają. Niezerowe saldo nie blokuje operacji. Powtórzenie daje 204,
brak członkostwa 404; administrator musi wcześniej przekazać rolę (409).
Usunięcie nie jest banem: ważne zaproszenie pozwala wrócić. Brak nowych powiadomień.
Zapisy korzystają z GroupingTransactionCoordinator, blokując najpierw grupę.
Nie ma migracji ani obliczania/utrzymywania sald w tym serwisie.

## Rozliczenia ręczne i agregaty — 2026-11-02
GroupSettlementService implementuje CRUD rozliczeń wykonanych poza aplikacją.
Aktualizuje wkład przez osobny GroupMemberAggregateService we wspólnej transakcji.
Obie strony mogą być historycznymi członkami; wykonujący musi być aktualnym
członkiem i nadawcą albo administratorem. Przy edycji zwykły użytkownik pozostaje
nadawcą przed i po zmianie. Data nie może być przyszła, kwota nie jest ograniczana
saldem. BankTransactionId niepusty daje 400; Banking i wyłączność źródeł pozostają
na osobną turę. Powiązane historyczne rekordy można odczytać lub usunąć; PUT
z null usuwa istniejące powiązanie.

IGroupMemberAggregateService udostępnia ApplySettlementChangeAsync i
ApplyExpenseChangeAsync(previous,current). Null po jednej stronie oznacza
utworzenie/usunięcie; obie null są błędem. Kontrakty wartości należą do Grouping,
bez encji Expense. Płatnik/udziały aktualizują TotalPaid/TotalShare, nadawca/odbiorca
TotalSettlementsSent/Received. Różnice są scalane per użytkownik i sprawdzane
przed mutacją; wynik nie może być ujemny ani przekraczać numeric(18,2).
Historyczne członkostwa są zachowane, brakujące nie są tworzone.
Serwis wymaga aktywnego koordynatora (EnsureActive), blokuje grupę, nie robi
SaveChanges ani commitu. Wywołanie musi nastąpić dokładnie raz ze zmianą źródła.
Expense stosuje tę samą transakcję i blokadę grupy.
ExpenseDbContext jest dołączany dla tworzenia wydatku od 2026-11-03.
Edycja/usuwanie Expense stosują ten sam koordynator; odbudowa/uzgadnianie agregatów pozostają do wykonania. Od 2026-11-03 utworzenie rozliczenia tworzy SettlementReceived (5)
wyłącznie dla ToUserId różnego od autora wpisu, również dla byłego członka.
Nieaktywne konta pomija NotifyAsync. GroupSettlementService jawnie dołącza
Notifications do transakcji koordynatora; błąd zapisu powiadomienia/odbiorcy
wycofuje rozliczenie i agregaty. Edycja i usunięcie nie tworzą powiadomień.
CHECK typów rozszerza istniejąca AddGroupingModel; CompleteExpenseModel
nie wykonuje operacji na Notifications.

## Prywatność powiązania bankowego rozliczenia — 2026-11-02
Grouping zależy od publicznego IBankingReadService, bez zależności odwrotnej.
Lista, szczegóły i odpowiedzi zapisów ujawniają BankTransactionId tylko po
poprawnym odczycie dla ID zalogowanego użytkownika (nie nadawcy rozliczenia).
Administrator grupy nie uzyskuje uprawnień do cudzej bankowości. Brak odczytu
lub kontrolowany błąd Banking daje null bez ukrywania samego rozliczenia.
Nieoczekiwane wyjątki propagują się bez fallbacku ujawniającego ID.
Sprawdzenia listy są kolejne, z lokalnym cache dla jednej odpowiedzi.
Brak nowego interfejsu przypisań, tabeli i migracji. POST/PUT nadal odrzucają
niepuste BankTransactionId. Edycja/null i DELETE historycznych powiązań
pozostają dozwolone; wymagają zastąpienia claim/release przy wdrożeniu Banking.
Walidacja wskazuje konkretne pola zamiast wspólnego komunikatu bankowego.

## Migracje po rebase Expense/Grouping — 2026-11-03

Po `20261102215301_AddGroupingModel` występuje
`20261103165622_CompleteExpenseModel`, wygenerowana z końcowego modelu Expense.
Usunięto tymczasową `AddTemporaryExpenseAggregationSupport` oraz zastąpiono
siedem migracji gałęzi Expense jedną. Grouping/Limits i migracja AddGroupingModel
z master pozostają bez zmian. Snapshot/designer wygenerowano ponownie przez EF.

Expense zachowuje `_id`, `name`, `transaction_time`, `fk_*`, wymaganego
`FkPayerId`, `Version`, metadane pozycji, kategorie bez Color i udziały procentowe
`Percentage` numeric(5,2). Kwoty udziałów są wyliczane przez Expense.
Migracja uwzględnia ograniczenia historycznego SQL init, których nie śledził
snapshot: zachowuje istniejące FK i CHECK ilości, usuwa CHECK dodatniej ceny,
a Down przywraca FK przypisania pozycji do użytkownika i CHECK ceny.
Up/Down wymagają pustych wydatków, pozycji i udziałów; nie konwertują danych ani
nie zgadują płatnika. Konflikty kategorii powodują rollback migracji.
Kolejność pustej bazy: SQL init → wszystkie migracje → seed. Seed przelicza
syntetyczne agregaty Grouping z procentów; nie utrzymuje ich podczas działania API.
Nie jest to migracja przyrostowa dla bazy po usuniętym łańcuchu.

Tworzenie Expense jest już połączone z koordynatorem, agregatami i rzeczywistymi
metadanymi Grouping. Edycja/usuwanie stosują tę samą transakcję. ExpenseAdded
jest podłączone przy tworzeniu; edycja i usuwanie nie powiadamiają.
Wyłączność bankowego źródła między Expense i rozliczeniem pozostaje osobnym zadaniem.

## Sugestie rozliczeń — 2026-11-02
GET /api/groups/{groupId}/settlements/suggestions: aktywny członek
niearchiwalnej grupy. Spójny odczyt grupy, autoryzacji i wszystkich agregatów
w jednym SQL AsSingleQuery/AsNoTracking, bez blokad zapisów i transakcji
koordynatora. Uwzględnia także byłych członków. Saldo = Paid - Share + Sent -
Received. Suma musi wynosić dokładnie zero, a końcowe liczniki Sent/Received
muszą mieścić się w numeric(18,2); w przeciwnym razie 409.
Osobny kalkulator stosuje deterministyczne największy dłużnik/największy
wierzyciel (kolejki priorytetowe, remisy po UserId), O(n log n), decimal.
Nie gwarantuje minimum; DTO jawnie zwraca optimalityGuaranteed=false.
Sugestie są odczytem i nie stanowią zlecenia płatności ani zapisów rozliczeń.
Bez migracji, Banking, odczytów Expense i odbudowy agregatów.
Kontrakty HTTP i sortowanie grup: [README Grouping](../backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/README.md).

## Dostęp do danych Grouping — 2026-11-02
Serwisy biznesowe Grouping kierują zapytania i operacje na zbiorach do repozytoriów.
SaveChangesAsync pozostaje w serwisie jako granica jednostki pracy; wspólną
transakcją i blokadami przepływu zarządza GroupingTransactionCoordinator.
Blokada SQL grupy należy do GroupRepository. Listowanie grup wraz ze stanem
członkostwa korzysta z GroupMembershipQuery i GroupMembershipEntry, bez zależności
repozytorium od DTO HTTP. Odczyty projekcyjne są bez śledzenia.
Serwisy zwracają wspólne GroupingResult<T>/GroupingResult. Kontrolery mapują je
na istniejące DTO i statusy; wyniki wewnętrzne nie są kontraktem HTTP.

## Rejestr przypisań Banking — 2026-11-03

Banking posiada BankTransactionAssignment i filtrowanie odczytu według typu
przypisania. Model i konfiguracja są wspólne dla BankingDbContext oraz
AppDbContext, bez zależności Banking od Expense/Grouping i bez polimorficznego FK
konsumenta. [Kontrakt Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#transaction-assignments--2026-11-03)
opisuje schemat, semantykę flag i wewnętrzny IBankTransactionAssignmentService.
Serwis udostępnia odczyt, idempotentne przypisanie oraz warunkowe zwolnienie.
Zapisy wymagają jawnej transakcji scoped BankingDbContext; serwis jej nie otwiera,
nie zatwierdza ani nie wycofuje i nie zapisuje innych oczekujących zmian EF.
Repozytorium wykonuje SQL od razu, blokując rekord BankTransaction na czas
transakcji i rozstrzygając konflikty bez nadpisywania konsumenta.
Scoped BankingDbContext współdzieli połączenie z koordynatorem Grouping.
Expense dołącza go do transakcji i utrzymuje przypisania przy tworzeniu,
edycji i usuwaniu. Konteksty fabryki używane do prób/połączeń zachowują osobne
połączenia. GroupSettlement nadal odrzuca nowe powiązania bankowe; integracja
tego konsumenta pozostaje do wykonania. Sam rejestr nie zapewnia spójności
z kolumnami źródłowymi konsumentów.

## Inicjalizacja użytkownika Tink przy łączeniu banku — 2026-11-07

Rejestracja i logowanie nie wywołują Tink. `BankingConnectionService` przy
`POST /api/banking/connection-attempts` zapewnia mapowanie przez publiczny
kontrakt `UserBankingProfileService.EnsureTinkUserAsync` i adapter Tink.
Blokada PostgreSQL serializuje inicjalizację użytkownika, a stabilny
external_user_id umożliwia odzyskanie zdalnej tożsamości po błędzie zapisu.
User nadal nie ma zależności od Banking. Osobny endpoint inicjalizacji usunięto.
[Kontrakt i błędy](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#tink-user-initialization-on-connection-start--2026-11-07).
