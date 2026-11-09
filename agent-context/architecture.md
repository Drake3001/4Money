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
Szablon API `.env.example` zawiera tylko poświadczenia, także SMTP. Niesekretne
wartości domyślne znajdują się w `appsettings.json`, a zmienne środowiskowe
je nadpisują. Lokalny `.env` nie jest szablonem do kopiowania sekretów do repozytorium.

## Obecna integracja Receipt i workera OCR

Kontrakt serwisu Receipt zaktualizowano 2026-11-02. Receipt ma
`IReceiptService` i `IReceiptAnalysisService` (HTTP), pogrupowane w
`Services/ReceiptService` i `Services/ReceiptAnalysisService`, ze wspólnym
namespace `ZPI4M.Receipt.Services`. Kontrakt S3 `IObjectStorage` i jego
implementacja znajdują się w `ZPI4M.Shared.Storage`.
Receipt nie udostępnia własnych endpointów HTTP. Zgodnie z decyzją z 2026-11-02
endpointy Expense będą korzystać z `IReceiptService`; wywołania z Expense
nie są jeszcze zaimplementowane. Jeden wydatek może mieć najwyżej jeden paragon.

`ReceiptService.CreateAsync` wymaga argumentu `bool runOcr`. Dla `true` wykonuje
OCR → upload S3 → zapis własnego kontekstu; dla `false` pomija OCR i zapisuje
`ExtractionResult = null`. Zwraca `ReceiptCreationResult` z `ReceiptId` i opcjonalnym
`JsonElement? ExtractionResult`, obejmującym pełną odpowiedź workera
(`receipt`, `warnings`, `needs_review`). Zapisany JSON odpowiada zwróconej analizie.
Odczyt rekordu udostępnia `GetByIdAsync`, podpisany URL — `GetFileUrlByIdAsync`,
a usuwanie — `DeleteAsync`. Nadpisanie lub dopisanie danych wydatku oraz ręczna
korekta należą do przyszłego serwisu Expense; surowy wynik OCR pozostaje w Receipt.
Nie jest to transakcja obejmująca magazyn i bazę. Po błędzie zapisu pozostawia
przesłany plik; przy usuwaniu najpierw usuwa rekord, potem próbuje usunąć plik.
Uprawnienia dostępu do paragonu nie są egzekwowane przez ten serwis.
Ograniczenia i zachowanie przy błędach opisuje [status](implementation-status.md#receipt-ocr-i-kategoryzacja--przegląd-2026-10-29).

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
| Kontenery aplikacji | Jeden containers/local/docker-compose.yml: PostgreSQL, backend .NET, SeaweedFS 4.48 i worker OCR; jedna konfiguracja containers/local/.env; wewnętrzna komunikacja po nazwach usług |
| Web | Docelowo React; brak aplikacji frontendowej w obecnym repo |
| Mobile | Uzgodniono React + Capacitor; Stos technologiczny.txt nadal wymienia alternatywę React Native, brak aplikacji w repo |
| OCR i storage | Worker FastAPI z OCR i kategoryzacją produktów; Receipt opcjonalnie wywołuje POST /api/v1/extract i korzysta z magazynu zgodnego z S3; dostęp HTTP planowany przez Expense; brak potwierdzenia pełnego przepływu w tej weryfikacji |
| Automatyzacja | GitHub Actions, pre-commit, narzędzia Python zarządzane przez uv |
| Testy backendu | Jedenaście projektów: po trzy User, Banking i Notifications (UnitTests, Tests, IntegrationTests) oraz dwa Receipt (UnitTests, IntegrationTests); CI dostarcza PostgreSQL 15 dla testów bazy |
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
- [Compose](../containers/local/docker-compose.yml)
- [Infrastruktura Proxmox i jej proces wdrożenia](../proxmox-infra/README.md)

## Worker synchronizacji Banking

Refresh on demand pozostaje częścią modularnego monolitu. BackgroundService
w hoście API przetwarza trwałe zadania PostgreSQL; kontroler tylko zleca zadanie
i udostępnia jego wynik. Serwis/processor koordynuje adapter Tink i repozytorium,
które przejmuje zadania przez lease oraz zapisuje dane atomowo dla rachunku.
Nie ma zewnętrznej kolejki. [Kontrakt i parametry workera](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand).

## Model grup po zatwierdzonym planie (2026-10-28)

Grouping posiada Group, GroupMember (SQL group_user), GroupSettlement i
GroupInvitation; Limits posiada GroupLimit obok niezmienionego CategoryGroupLimit.
Expense posiada płatnika i udziały kwotowe. Mapowania wewnątrz modułów są w ich
konfiguracjach; nowe relacje między modułami są wyłącznie w AppDbContext i mają
DeleteBehavior.NoAction. Nie dodano zależności projektów ani nowego CRUD/API.
Szczegóły pól i przyszłe niezmienniki opisuje [domena](domain.md#zatwierdzony-model-grup--2026-10-28).
Agregaty nie są utrzymywane automatycznie; przyszły koordynator musi zapisywać
źródła i agregaty atomowo oraz umożliwiać ich odbudowę. Członkostwo, suma udziałów,
waluta, właściciel transakcji i wyłączność bankowego źródła pomiędzy wydatkiem
oraz rozliczeniem nie są jeszcze egzekwowane przez serwis.

Storage jest współdzielony w ZPI4M.Shared.Storage: IObjectStorage, S3ObjectStorage,
FileUrlResult i konfiguracja STORAGE_*. Klucze generuje moduł wywołujący;
Receipt zachowuje prefiks receipts/. Testy adaptera: ZPI4M.Shared.UnitTests.

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
Przyszły Expense musi stosować tę samą transakcję i blokadę grupy.
Nie podłączono ExpenseDbContext ani CRUD Expense. Brak odbudowy/uzgadniania
agregatów. Od 2026-11-03 utworzenie rozliczenia tworzy SettlementReceived (5)
wyłącznie dla ToUserId różnego od autora wpisu, również dla byłego członka.
Nieaktywne konta pomija NotifyAsync. GroupSettlementService jawnie dołącza
Notifications do transakcji koordynatora; błąd zapisu powiadomienia/odbiorcy
wycofuje rozliczenie i agregaty. Edycja i usunięcie nie tworzą powiadomień.
CHECK typów rozszerza istniejąca AddGroupingModel; tymczasowa migracja Expense
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

## Podział migracji Grouping i Expense — 2026-11-02
Aktualny koniec łańcucha:
1. 20261102215301_AddGroupingModel — docelowy model Grouping i zachowane Limits.
2. 20261102215328_AddTemporaryExpenseAggregationSupport — tymczasowe minimum Expense.

Zastępują usunięte migracje 20261028191002_ApprovedGroupingModel i
20261031165114_AddGroupDescriptionIconsAndArchive. Wcześniejsze opisy tych
migracji są historyczne; nie wykonuj ich procedur cofania na nowym łańcuchu.

Expense zachowuje Id/_id, Name/name, TransactionTime/transaction_time,
fk_group_id, fk_receipt_id, fk_bank_transaction_id i udziały z fk_expense_id,
fk_user_id. Tymczasowe rozszerzenia: FkPayerId/fk_payer_id (wymagany,
indeks ix_expense_fk_payer_id, FK User) oraz Amount/amount numeric(18,2)
zamiast Percentage. Nie ma zmian nazw kolumn niezwiązanych z agregacją.
Grouping nie zależy od encji Expense; jego ExpenseContribution nadal używa
PaidByUserId jako nazwy kontraktu wartości.

Migracja Grouping nie zmienia tabel Expense. Jej Up/Down wymagają pustych
grup i członkostw. Tymczasowa Expense wymaga pustych wydatków/udziałów przy
Up/Down; nie konwertuje historycznych danych ani nie zgaduje płatnika.
Seed działa dopiero po obu migracjach i przelicza agregaty z danych syntetycznych.

Przy integracji właściwego PR Expense należy uzgodnić model, relacje, repozytoria,
tymczasową migrację, designer/snapshot, seed i testy. Samo usunięcie ostatniej
migracji nie cofa zastosowanego schematu. Lokalna baza po starym łańcuchu wymaga
kontrolowanego odtworzenia (SQL init → migracje → seed), bez kasowania kontenera
lub wolumenu. Nie zmieniaj ręcznie pojedynczych wpisów historii EF.

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
Kontrakt frontendowy: agent-context/grouping-frontedn.md.

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
Banking zachowuje osobne połączenie. Integracja z przepływami biznesowymi oraz
dołączenie kontekstu do ich transakcji pozostają na później; przyszły koordynator
musi zapewnić atomowy zapis/usunięcie przypisania razem z powiązaniem konsumenta.
Sam rejestr nie zapewnia spójności z kolumnami źródłowymi konsumentów.

## Inicjalizacja użytkownika Tink przy łączeniu banku — 2026-11-07

Rejestracja i logowanie nie wywołują Tink. `BankingConnectionService` przy
`POST /api/banking/connection-attempts` zapewnia mapowanie przez publiczny
kontrakt `UserBankingProfileService.EnsureTinkUserAsync` i adapter Tink.
Blokada PostgreSQL serializuje inicjalizację użytkownika, a stabilny
external_user_id umożliwia odzyskanie zdalnej tożsamości po błędzie zapisu.
User nadal nie ma zależności od Banking. Osobny endpoint inicjalizacji usunięto.
[Kontrakt i błędy](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#tink-user-initialization-on-connection-start--2026-11-07).
