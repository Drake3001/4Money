# Architektura systemu

Ten dokument opisuje granice i wspólne wzorce implementacji. Pojęcia i reguły
biznesowe należą do [domeny](domain.md), uruchamianie do [development](development.md),
a możliwości i braki do [statusu](implementation-status.md). Przy zmianie
przepływu sprawdź rzeczywiste DI i wywołania, nie tylko listę ProjectReference.

## Modularny monolit i warstwy

Backend jest solucją .NET z jednym hostem API, modułami, Shared i wspólnym
projektem migracji Database. Worker OCR jest osobną usługą Python, frontend
osobnym klientem HTTP. Podział katalogów backendu nie ustanawia mikroserwisów,
a oddzielne DbContexty nie gwarantują niezależnych transakcji.

| Obszar | Odpowiedzialność i granica |
| --- | --- |
| User | Tożsamość, sesje, konto, SMTP, Google; publiczne lookupy i profil Banking |
| Grouping | Grupy, uprawnienia, członkostwa, zaproszenia, salda, rozliczenia, koordynator |
| Expense | Formularz zakupu, obliczenia, źródła, historia, uprawnienia odczytu, odczyty raportowe |
| Receipt | Analiza pliku, rekord oryginalnego OCR, dostęp do prywatnego obiektu przez adapter |
| Banking | Próby/połączenia, lokalne odczyty, synchronizacja Tink i rejestr wykorzystania transakcji |
| Notifications | Inbox, odbiorcy, zapis powiadomienia i indywidualny odczyt |
| Limits | Definicje budżetów, wykorzystanie, alerty i sweep |
| Reports | Cztery raporty HTTP przez IExpenseAggregationService; bez bezpośrednich odczytów tabel Expense |
| Shared | ICurrentUser, storage, przetwarzanie ikon i wspólne pomocniki |
| Database | Pełny model EF, relacje między modułami, migracje i snapshot |

Kontrolery korzystają z serwisów i DTO. Serwisy walidują zachowanie, koordynują
operacje i korzystają z repozytoriów; zapytania oraz operacje na zbiorach należą
do repozytoriów. Modele persystencji nie są kontraktami HTTP. Nie przenoś SQL
do kontrolera ani nie pobieraj całej tabeli tylko po to, by filtrować ją w serwisie.
Repozytorium nie powinno samodzielnie zatwierdzać jednostki pracy wyższego poziomu.

Stosuj istniejące publiczne interfejsy modułu zamiast odczytywać cudze tabele
bezpośrednio. Repozytoria są zwykle grupowane jako
`Repositories/<Nazwa>Repository/` z interfejsem, implementacją i opcjonalnym Queries;
namespace odpowiada katalogowi. Zachowuj zastaną konwencję, nie twórz równoległych
adapterów tylko dla innej nazwy katalogu. Lookup User dla wielu ID powinien
pozostać zapytaniem zbiorczym, nie osobnym wywołaniem dla każdego odbiorcy.

Expense korzysta z kontraktów Grouping, Banking, Receipt i User. Grouping korzysta
z Notifications i User oraz publicznego odczytu Banking. Notifications korzysta
z User, ale nie importuje Expense. Limits konsumuje odczyty Expense i konteksty
Grouping/User/Notifications. Nie wprowadzaj zależności odwrotnej Expense → Limits:
Expense udostępnia `IExpenseChangeHandler`, a host rejestruje obsługę Limits.
Relacja FK Notification → Expense w pełnym modelu bazy nie wymaga zależności
projektowej Notifications → Expense i nie uzasadnia cyklu modułów.

Host składa DI oraz kontrolery przez ApplicationPart. Błąd braku rejestracji nie
powinien być obchodzony mockiem w produkcyjnym przepływie. Implementacje User
Lookup/BankingProfile oraz serwis Notifications mają własne spójne katalogi;
nowe zależności dodawaj w ich istniejącym punkcie rejestracji.

## Tożsamość i granice bezpieczeństwa

`HttpCurrentUser` jest wspólną scoped implementacją ICurrentUser opartą na
IHttpContextAccessor. Bieżący aktor pochodzi z niej, nie z parametru formularza
ani ponownego parsowania claimu. Nie zastępuje walidacji podpisu, czasu i sesji JWT.
Autoryzacja grupy, uprawnienie płatnika i dostęp do rachunku pozostają odrębnymi
kontrolami. Administrator grupy nie staje się właścicielem cudzych danych Banking.

Błędy biznesowe są mapowane na ProblemDetails, błędy pól na ValidationProblemDetails.
Niektóre moduły zwracają również code i identyfikator operacji. Przy rozbudowie
zachowaj kontrakt danego modułu; ujednolicenie formatów jest osobnym zadaniem.
Nieoczekiwany wyjątek nie może uruchamiać fallbacku ujawniającego prywatne ID.
Kontrolowane niedostępności odczytu nie powinny być zamieniane na fałszywe zero
kosztu lub potwierdzenie, że użytkownik ma uprawnienia.

## Transakcje między modułami

Host przekazuje jedną scoped NpgsqlConnection do ExpenseDbContext,
GroupingDbContext, NotificationsDbContext, ReceiptDbContext, LimitsDbContext
i scoped BankingDbContext. Konteksty nie są właścicielami połączenia; zwalnia je DI.
User, migracyjny AppDbContext i konteksty tworzone przez fabrykę Banking używają
osobnych połączeń. Nie próbuj dołączyć kontekstu z fabryki do transakcji na innym
połączeniu. Fabryka służy m.in. zapisom prób i połączeń Banking.

Wspólne połączenie jest warunkiem, nie automatyczną transakcją. Właścicielem
zapisów Grouping/Expense/Limits jest `IGroupingTransactionCoordinator`.
`ExecuteAsync(action, succeeded, cancellationToken)` rozpoczyna transakcję,
a końcowy sukces zależy od wyniku zewnętrznego i wszystkich zagnieżdżonych operacji.
Dla wyników HTTP succeeded oznacza 2xx. `Reject` sygnalizuje odmowę także przed
zapisem wewnętrznej operacji. Wyjątek lub odmowa zagnieżdżona blokuje commit,
również gdy wywołujący przechwyci wyjątek.

Nie otwieraj niezależnej transakcji EF przed koordynatorem. Inny aktywny owner
jest odrzucany przed mutacją. `EnlistAsync` sprawdza tożsamość połączenia i
transakcji, po czym używa UseTransactionAsync; sam SaveChangesAsync nie zastępuje
tego kroku. Notifications dołącza `EnlistNotificationsAsync`. Wywołany moduł może
wykonać SaveChanges, ale nie zatwierdza transakcji koordynatora.

Operacje na połączeniu są sekwencyjne. Nie używaj Task.WhenAll dla tych kontekstów
ani równoległych serwisów w tym samym scope. Po rollbacku lub nieudanym zakończeniu
utwórz nowy scope DI. Koordynator odrzuca ponowne zapisy zakończonej niepowodzeniem
jednostki pracy; nie czyści globalnie ChangeTracker i nie naprawia encji w pamięci.

### Checklista zapisu między modułami

1. Ustal jednego właściciela transakcji i autoryzuj aktora. Odczyty preview nie
   rozpoczynają procesu mutacji ani nie rezerwują danych.
2. Wejdź do ExecuteAsync, dołącz wymagane konteksty przez EnlistAsync i pozyskaj
   blokady w kolejności obowiązującej dla danego procesu.
3. Pod blokadą ponownie sprawdź członkostwo, archiwum, wersję i pozostałe warunki,
   które mogły zmienić się od wcześniejszego odczytu.
4. Zapisz źródło i agregaty. Gdy nowe powiadomienie ma FK do nowego źródła,
   źródło musi być zapisane przed NotifyAsync. Dołącz Notifications jawnie.
5. Uruchom wymagane handlery, w tym alerty Limits. Null z NotifyAsync oznacza
   brak aktywnych odbiorców i dopuszcza commit; błąd zapisu wymaga rollbacku.
6. Zatwierdź wyłącznie w koordynatorze po powodzeniu całego przepływu.
   Callbacki sprzątania rejestruj przed zakończeniem aktywnej jednostki pracy.
7. Testuj nie tylko sukces, ale też błąd powiadomienia, błąd po jego zapisaniu,
   anulowanie, brak odbiorców oraz zewnętrzny rollback po sukcesie wewnętrznym.
   Nie mogą zostać źródło, salda, przypisania bankowe, alert-state ani inbox.

### Expense, agregaty i Banking

ExpenseService współdzieli przygotowanie formularza i zapisu, delegując odczyty
do ExpenseQueryService i obliczenia do IExpenseCalculationService. Grouping
otrzymuje neutralne ExpenseContribution z kwotami, bez encji Expense i bez
samodzielnego odczytu jego tabel. GroupExpenseContributionService pod blokadą
grupy sprawdza uprawnienia i stosuje różnicę wkładu. AggregateService wymaga
aktywnego koordynatora, lecz nie robi SaveChanges ani commitu.

Create/edit/delete Expense aktualizują wydatek, pozycje, udziały i agregaty
atomowo. PUT/DELETE odwracają poprzedni wkład. Expense dołącza również scoped
BankingDbContext do claim/release źródła; przypisanie jest wycofywane z resztą.
Rejestr Banking zapewnia wyłączność między konsumentami, ale GroupSettlement
nadal odrzuca nowe bankTransactionId. Nie wdrażaj połowy tego procesu tylko
przez udostępnienie pola w DTO.

Tworzenie Expense wywołuje GroupExpenseNotificationService dla pozostałych
aktualnych członków, z nazwami z lookupu User. Serwis nie czyta tabel Expense.
Edycja/usuwanie nie wysyłają ExpenseAdded, ale wywołują ocenę budżetów.
Notifications tworzy odbiorców tylko dla aktywnych kont. Operacje biznesowe,
alert-state i inbox pozostają częścią tej samej transakcji.

### Limits i kolejność blokad

LimitWriteExecutor dołącza LimitsDbContext do koordynatora. Grupowe zapisy i sweep
korzystają z blokady grupy; osobiste z uporządkowanych advisory locks właścicieli.
Expense zachowuje kolejność grupa → właściciele, aby nie tworzyć odwrotnego
porządku z zapisami budżetów. LimitOverlapPolicy sprawdza konflikty przed mutacją.
Nowa/włączona definicja, jej wersja, alert-state i inbox muszą zatwierdzić się
razem. Polityka konfliktów i ograniczenia PostgreSQL dotyczą tylko kalendarzowych;
Custom współistnieją niezależnie. Zapisy nie modyfikują innych definicji.

LimitUsageService korzysta z publicznych odczytów Expense i jego reguł groszowych,
nie utrzymuje własnych trwałych liczników kosztu. Wewnętrzny odczyt grupowy nie
podszywa się pod administratora: GroupBudgetContextService zapewnia zaufany
kontekst workera po sprawdzeniu aktywności pod blokadą. GET nie tworzy stanu alertu.

Sweep ma świeży scope i transakcję na kandydata, limit pracy, paginację keyset
i kursor w pamięci. Wywołania jednej instancji są sekwencyjne; worker czeka na
rzeczywiste zakończenie przebiegu także po timeoutcie, bez semafora w serwisie.
Porażka przesuwa kursor, aby pojedyncza definicja nie blokowała
innych. Stan w bazie i te same blokady chronią wiele instancji. Nie ma odnawiania
budżetów, generowania okresów, historycznego backfillu ani trwałej kolejki.
Kontrakt i konfiguracja: [Limits](../backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md).

## Pliki, Receipt i worker OCR

IObjectStorage jest wspólnym adapterem Shared.Storage; moduł jest właścicielem
kluczy i cyklu życia swoich obiektów. Nie utrwalaj podpisanego URL jako klucza.
STORAGE_ENDPOINT służy operacjom wewnętrznym, STORAGE_PUBLIC_ENDPOINT adresowi
podpisanego GET widocznego z przeglądarki. To może być ten sam bucket przy
różnych endpointach. Podpisanie URL bez HEAD nie potwierdza istnienia obiektu.

Receipt udostępnia serwisy analizy i zapisu, bez osobnego kontrolera Receipt/Storage.
HTTP obsługuje Expense. Analiza wywołuje worker bez zapisu i podpisuje oryginalny
wynik związany z użytkownikiem oraz plikiem. Zapis weryfikuje proof, zachowuje
oryginalny OCR i osobno zatwierdzony formularz. ReceiptDbContext jest jawnie
dołączany do wspólnej transakcji; prawa odczytu sprawdza Expense.

Upload odbywa się po walidacji, przed transakcją zapisu. PostgreSQL i S3 nie mają
wspólnego commitu. Nowy plik sprząta się po potwierdzonym rollbacku, zastąpiony
lub odpięty po potwierdzonym commicie. Przy niepewnym wyniku commitu zachowuje się
pliki do uzgodnienia. Błąd cleanupu jest best effort i nie zmienia potwierdzonego
sukcesu; może zostać osierocony obiekt. Nie ma workera automatycznego uzgadniania.
Ta sama zasada dotyczy własnych ikon User/Grouping; presetów nie usuwamy.

Shared.Images.IconImageProcessor weryfikuje sygnaturę, dekoduje dozwolone formaty,
stosuje orientację, usuwa metadane i zapisuje zmniejszony WebP. Moduły zachowują
uprawnienia, prefiksy i sprzątanie. Szczegóły limitów i konfiguracji ikon są w
[User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md#zdjęcia-profilu-i-prefiksy-storage).

Worker FastAPI ma własne środowisko Python/uv i kontrakt multipart, obsługując
fragmenty jednego paragonu. Klient .NET wysyła jeden plik i sprawdza strukturę
odpowiedzi. Domyślny vision_ocr łączy RapidOCR z Gemini przez OpenRouter;
propozycje kategorii mapuje Expense. Worker nie tworzy encji .NET, nie ma własnego
auth i powinien działać w prywatnej sieci. Limity i pipeline należą do
[README workera](../workers/extraction_worker/README.md), nie konfiguracji UI.

## Konto i komunikacja zewnętrzna

User ma własne transakcje konta/sesji. Trigger PostgreSQL tworzy zwykłą grupę
startową „Wydatki osobiste” w PLN po INSERT użytkownika, także z seeda lub SQL.
Grupa i członkostwo uczestniczą w tym samym zapisie. Nie ma backfillu istniejących
kont; Down triggera nie usuwa grup. Nie zastępuj tego drugim tworzeniem grupy
w serwisie rejestracji.

SMTP nie jest transakcją PostgreSQL. Zwykła rejestracja i wystawianie tokenów
wysyłają przed commitem; błąd wycofuje zapis, ale mail mógł zostać przyjęty przed
późniejszym błędem bazy. Rejestracja Google wymagająca potwierdzenia wysyła po
commicie, zachowując konto przy awarii. Nie ma outboxa ani gwarancji dostarczenia.
Google challenge jest lokalny dla procesu, nie współdzielony przez instancje.
Procedury i ograniczenia wdrożenia: [User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md).

Banking rozdziela trwały stan prób/połączeń od pracy refresh. Worker synchronizacji
korzysta z lease, heartbeat i kursorów; odczyty publiczne korzystają z lokalnych
snapshotów. Nie utożsamiaj połączenia z bankiem, potwierdzenia zgody i zakończenia
importu. Integracja Tink i diagnostyka są w [Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md).

## Frontend i granica przeglądarki

Frontend React/TypeScript/Vite jest klientem API, nie drugim właścicielem reguł
backendu. Klient HTTP obsługuje Bearer, współdzielone odświeżenie sesji po 401
i pojedyncze ponowienie. Sesja jest w localStorage, ID token Google w pamięci
komponentu. `src/mocks/` zawiera typy/pomocniki, nie symulację ruchu HTTP.

Nawigacja jest stanem React; linki e-mail i zaproszenia mają osobną interpretację.
Proxy dev/preview umożliwia wspólny origin; jawny adres innego origin wymaga CORS.
Host nie rejestruje obecnie ogólnej polityki CORS. Cookie Google wymaga zachowania
credentials i reguł SameSite; reverse proxy musi poprawnie przekazać HTTPS.
Zmiana adresu API nie może być obchodzona umieszczeniem sekretu w VITE_*.

Przy powiązaniu bankowym innego płatnika UI pokazuje właściciela bez pobierania
prywatnych szczegółów, zachowuje ID i kwotę Expense, blokuje płatnika/kwotę oraz
zmianę źródła. Ograniczenia formularza nie zastępują autoryzacji serwera.
Banking otwiera Tink w nowej karcie i odpytuje próbę w pierwotnej; callback HTML
jest samodzielną stroną wyniku, a JSON zachowuje kontrakt API. Moduł web ma
[osobną instrukcję](../frontend/README.md).

## Persystencja

AppDbContext składa konfiguracje modułów i relacje między nimi. Historyczny SQL
init zawiera ograniczenia nieobecne w snapshocie; generowane DDL wymaga przeglądu.
Pierwsza migracja jest pusta i zakłada SQL init, a nie samodzielny pełny baseline.
Modele, migracje i seed mają różne role: seed usuwa dane, nie naprawia schematu.

Aktualny łańcuch kończą UserImprovements, NormalizeExpenseCategoriesAndAddDeposit
i AddLimits. Konsolidacja Limits zachowuje wcześniejsze migracje;
Up/Down odmawiają danych w zmienianych tabelach. Procedury, rzeczywiste ID i
postępowanie z bazą po usuniętych migracjach: [migracje](development.md#modele-i-migracje).
Nie odtwarzaj wolumenu ani nie edytuj historii EF jako elementu zwykłego review.

## Agregacje Expense

Reports korzysta z IExpenseAggregationService i parametryzowanego SQL w
ExpenseRepository. Agregacje i alokacja groszy są wykonywane w PostgreSQL.
Limits używa GetUsageTotalAsync; systemowy port IExpenseSystemAggregationService
wymaga aktywnego koordynatora i kontekstu grupy ustalonego pod blokadą przez
wywołującego. Oba moduły używają daty UTC Expense; Warsaw wybiera dzisiejsze
okno budżetu. Kontrakty i reguły dostępu: [Expense](../notes/EXPENSE_REPORTS.md).
