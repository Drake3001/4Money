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

Zgodnie z aktualnym opisem i diagramem Expense koordynuje dodawanie wydatku: przez
publiczny interfejs Receipt zleca odczyt paragonu, a przez interfejs Banking
pobiera transakcje do wyboru lub dopasowania. Expense odpowiada za utworzenie
wydatku i zapisanie powiązań. Receipt korzysta wyłącznie z modelu OCR,
a docelowo Banking korzysta z modułu/integracji OpenBanking. Receipt i Banking nie
zależą od Expense ani od siebie nawzajem. Są to ustalenia dotyczące współpracy
modułów i integracji; nie oznaczają, że projekty OCR lub OpenBanking już istnieją
w solucji.

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
Notifications; Notifications nie pobiera encji Expense. Ten przepływ jest
planowany, a nie zaimplementowany. Relacja FK Notification → Expense jest
konfigurowana wyłącznie we wspólnym AppDbContext; nie dodawaj referencji
projektu Expense do Notifications, bo utworzyłaby cykl zależności.

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
User nie rozszerza jego pól. Obecny kontrakt Banking dodaje `code` i opcjonalne
`attemptId`; ujednolicenie wymaga osobnej decyzji dotyczącej kontraktu. Kontrolery zwracają błędy biznesowe przez `Problem()`.
Walidacja DTO zwraca `ValidationProblemDetails` z błędami poszczególnych pól.
Host zapewnia `ProblemDetails` dla pustych błędów HTTP i nieobsłużonych wyjątków.

Schemat przepływu to `ApiControllers → Services → Repositories → baza`.
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
NotificationRecipient nie mają Queries. NotificationRecipientRepository przyjmuje
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

Od 2026-10-28 host przekazuje ExpenseDbContext, GroupingDbContext i
NotificationsDbContext tę samą scoped instancję NpgsqlConnection. Połączenie
zwalnia DI, a konteksty mają contextOwnsConnection: false. Pozostałe konteksty,
w tym AppDbContext migracji, zachowują konfigurację przez connection string.
Jest to przygotowanie do integracji: wspólne połączenie nie rozpoczyna ani nie
współdzieli automatycznie transakcji. Serwis koordynujący rozpoczyna transakcję,
dołącza pozostałe konteksty przez UseTransactionAsync i wykonuje końcowy commit
dopiero po powodzeniu wszystkich zapisów. NotifyAsync może zachować
SaveChangesAsync; w dołączonym kontekście nie zatwierdza ono transakcji
koordynatora. Błąd musi przerwać całą operację i spowodować rollback.
Operacje na wspólnym połączeniu wykonuj kolejno, bez Task.WhenAll i bez
otwierania zagnieżdżonych transakcji. Serwisy koordynujące Expense/Grouping
oraz testy atomowości między modułami nie są jeszcze zaimplementowane.

### Do wykonania przy implementacji Expense i Grouping

Przy zapisie wydatku z powiadomieniem oraz operacji grupowej z powiadomieniem
(np. dołączeniu członka) obowiązuje jedna transakcja obejmująca zapis biznesowy
i powiadomienie wraz z odbiorcami. W przyszłym serwisie koordynującym:

1. Korzystaj z kontekstów z tego samego zakresu DI i wspólnego połączenia.
2. Przed pierwszym zapisem rozpocznij transakcję przez BeginTransactionAsync
   na kontekście operacji biznesowej.
3. Dołącz NotificationsDbContext i każdy inny kontekst zapisujący dane tej
   operacji przez UseTransactionAsync(transaction.GetDbTransaction()).
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
dla danego konta. Wywołania SMTP odbywają się po zakończeniu zapisu/transakcji.
Serwisy User wysyłają wiadomości bezpośrednio przez wspólny IEmailSender.
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
Szablon API `.env.example` używa nazw zmiennych wielkimi literami i zawiera
ustawienia SMTP z przykładowymi wartościami; lokalny `.env` nie jest szablonem
do kopiowania sekretów do repozytorium.

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

Nazwy tabel i kolumn są mapowane jawnie, zwykle w snake_case. Przy zmianie
modelu sprawdź konfigurację, kontekst modułu i wspólny kontekst migracji.
Nie zakładaj, że sama zmiana właściwości C# aktualizuje istniejącą bazę.

SQL w `database/init` tworzy bazową strukturę przy pierwszej inicjalizacji
kontenera. Historia migracji rozwija ten stan. Procedura i ograniczenia
znajdują się w [instrukcji pracy](development.md#modele-i-migracje).

## Stos obecny i docelowy

| Obszar | Stan potwierdzony / założenie |
| --- | --- |
| Backend | C#, .NET 10, ASP.NET Core, EF Core, Npgsql |
| API | Obecnie OpenAPI w Development; Swagger i Postman wymieniono w planie narzędzi |
| Baza lokalna | PostgreSQL 15 Alpine w Docker Compose |
| Web | Docelowo React; brak aplikacji frontendowej w obecnym repo |
| Mobile | Uzgodniono React + Capacitor; Stos technologiczny.txt nadal wymienia alternatywę React Native, brak aplikacji w repo |
| OCR | Plan Python i integracja OCR; brak implementacji pipeline'u w obecnym repo |
| Automatyzacja | GitHub Actions, pre-commit, narzędzia Python zarządzane przez uv |
| Testy backendu | Dziewięć projektów: po trzy User, Banking i Notifications (UnitTests, Tests, IntegrationTests); CI dostarcza PostgreSQL 15 i zmienne połączeń dla wszystkich trzech modułów |
| Infrastruktura | Obecna konfiguracja Terraform dla Proxmox; Linux i Tailscale w środowisku developerskim |
| Prezentacja | AWS jako założenie docelowe, nie potwierdzone wdrożenie aplikacji |

Banking zawiera kontrolery, serwisy, adapter Tink, odczyt zgód i worker importu
rachunków oraz transakcji na żądanie. Publiczny `IBankingReadService` udostępnia
lokalne listy i szczegóły rachunków/transakcji przez DI, z wymaganym właścicielem
i osobnymi kontraktami danych w `Services/Contracts`. Odczyt nie wymaga Tink,
nie uruchamia synchronizacji. Expense może użyć
tego interfejsu do wyboru transakcji; tworzenie wydatku i powiązania pozostaje
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
- [Compose](../containers/docker-compose.yml)
- [Infrastruktura Proxmox i jej proces wdrożenia](../proxmox-infra/README.md)

## Worker synchronizacji Banking

Refresh on demand pozostaje częścią modularnego monolitu. BackgroundService
w hoście API przetwarza trwałe zadania PostgreSQL; kontroler tylko zleca zadanie
i udostępnia jego wynik. Serwis/processor koordynuje adapter Tink i repozytorium,
które przejmuje zadania przez lease oraz zapisuje dane atomowo dla rachunku.
Nie ma zewnętrznej kolejki. [Kontrakt i parametry workera](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand).
