# Stan implementacji i kwestie otwarte

## Bieżący przegląd — 2026-11-17

Podstawa: statyczny przegląd kodu i konfiguracji rewizji `fe2e8c0`, aktualizacje
do `c6e5b96` oraz lokalne zmiany frontendu z 2026-11-16/17. Aktualizacja dotyczy
Expense/OCR, kategorii, migracji i prezentacji Banking; pozostałe obszary
zachowują zakres wcześniejszego przeglądu. Tabela „Potwierdzony stan” opisuje bieżący
zakres. Datowane wpisy dalej zachowują historię konkretnych wdrożeń i testów;
ich wyniki nie są nową regresją całego backendu.

- **Web:** podłączone do API konto i Google Identity Services, grupy,
  zaproszenia, rozliczenia, wydatki z OCR, inbox i Banking. `src/mocks/`
  dostarcza typy i pomocniki; nie symuluje danych HTTP. Pulpit prezentuje
  bankowość i powiadomienia, bez raportowania Expense lub limitów.
- **Expense UI:** historia ma wyszukiwanie, sortowanie i filtry; kategorie
  pochodzą z API. Końcowa data filtra w UI jest zamieniana na następny dzień
  dla wyłącznego końca HTTP. Tworzenie prowadzi przez źródło ręczne, bankowe
  albo paragon. Formularz pozwala korygować produkty/kategorie OCR i bilansować
  udziały procentem, kwotą lub suwakiem z blokowaniem wybranych uczestników.
  API nadal zapisuje procenty. Podgląd HEIC korzysta z lokalnej konwersji.
  „Zniżka” ma walidację UI ujemnej ceny, ilości 1 i rabatu 0, a wybór kategorii
  ustawia ilość i rabat. Transakcja innego płatnika pozostaje widoczna jako
  powiązanie bez ujawniania jej szczegółów i bez możliwości jej zmiany w UI.
- **Banking UI:** zakładki Konta, Transakcje, Połączenia i Odświeżanie.
  Konta są filtrowane po nazwie i stanie oraz sortowane w przeglądarce;
  transakcje mają serwerową paginację, filtry i sortowanie datą/kwotą.
  Szczegóły transakcji otwierają wspólny modal; zachowano też osobny widok.
  Zgody pokazują stan dostawcy i możliwość przedłużenia, bez operacji odnowienia.
  Tink otwiera się w nowej karcie; główna odpytuje próbę co 3 s, refresh co 2,5 s.
  Callback dla Accept: text/html zwraca stronę sukcesu/niepowodzenia bez
  przekierowania; JSON zachowuje dotychczasowy kontrakt i statusy błędów.
  Błędy refreshu są opisane po polsku, także dla poszczególnych kont.
  Identyczne alerty żądań są scalane; wspólny alert nie ma przycisku ponowienia.
- **Inbox:** filtry odczytu, paginacja, odczyt pojedynczy/zbiorczy, licznik
  i podgląd dzwonka. Typy 1/4/5 prowadzą do wydatku, grupy lub rozliczeń grupy.
  Grouping tworzy polskie treści z nazwami pobieranymi przez lookup User;
  alerty Limits pozostają niepodłączone.
- **Konfiguracja i e-mail:** wymagany `FRONTEND_URL` zasila linki User
  i jest walidowany przy starcie API. Zastąpił starszy opis konfiguracji przez
  USER_EMAIL_FRONTEND_URL. Wiadomości mają logo MIME CID, tekstową alternatywę
  zachowującą URL przycisku i datę ważności w Europe/Warsaw.
- **Persystencja:** wspólne scoped połączenie Expense, Grouping, Notifications,
  Receipt i Banking; koordynator dołącza konteksty jawnie. Fabryka Banking
  dla prób/połączeń ma osobne połączenia. Expense utrzymuje rejestr przypisań
  bankowych atomowo ze swoim zapisem; GroupSettlement nadal odrzuca nowe
  bankTransactionId. Ostatnia migracja to
  `20261116211631_NormalizeExpenseCategoriesAndAddDeposit`.
- **Reports API (aktualizacja 2026-11-18):** cztery raporty GET korzystają
  z IExpenseAggregationService i agregacji SQL; szczegóły w sekcji Reports poniżej.
- **Pozostałe braki:** aplikacja mobilna/Capacitor, usługi i API Limits, UI Reports,
  alerty limitów, odbudowa agregatów, bankowe źródła rozliczeń, odłączanie banku
  i odnawianie zgód oraz produkcyjny obraz/hosting frontendu. Automatyczne
  dopasowanie paragonów i push pozostają rozszerzeniami poza MVP.

**Weryfikacja zmian frontendu z 2026-11-16/17:** `npm.cmd run typecheck`
zaliczone; po ostatnim usunięciu dwóch przycisków sprawdzono `git diff --check`.
Nie uruchamiano testów, builda Vite, przeglądarki ani usług zewnętrznych.
Aktualizacja dokumentacji z 2026-11-17 opiera się na przeglądzie kodu i diffów;
nie obejmuje nowych testów, migracji ani operacji na bazie i kontenerach.

**Historyczna weryfikacja z 2026-11-15:** `npm.cmd test`: **42 zaliczone, 0 błędów, 0 pominiętych**,
oraz `npm.cmd run build`: kontrola TypeScript i Vite zakończona powodzeniem.
Build ostrzega o pakiecie HEIC (~3,2 MB przed gzip), ładowanym na żądanie.
Testy korzystają z Node, SSR i syntetycznego HTTP; nie sprawdzają interakcji
przeglądarki. Nie uruchamiano nowych testów .NET/Python, bazy, migracji, seeda,
kontenerów ani rzeczywistych Google, SMTP, Tink, OCR i S3. Workflow backendu
nie wykonuje testów frontendu ani workera Python.

Źródła: [App](../frontend/src/App.tsx), [klient HTTP](../frontend/src/api/client.ts),
[Banking UI](../frontend/src/views/Banking.tsx),
[callback](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/ApiControllers/TinkCallbackController.cs),
[konfiguracja hosta](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs),
[Compose](../containers/local/docker-compose.yml),
[workflow](../.github/workflows/backend.yml).

## Historia zmian i wcześniejszej weryfikacji

**Rabat i „Zniżka” w UI — przegląd 2026-11-17:** rabat dotyczy całej pozycji,
jest kwotowy i niedodatni; puste pole oznacza 0. Nieliczbowy lub dodatni rabat
daje „Rabat musi być liczbą mniejszą lub równą zero.” Na kategorii „Zniżka”
(ID `30000000-0000-0000-0000-000000000020`) frontend wymaga ujemnej ceny także
po zaokrągleniu do groszy, ilości 1 i rabatu 0. Wybór tej kategorii w tworzeniu
lub edycji nadpisuje ilość na 1 i rabat na 0; nie zmienia ceny. Pola pozostają
edytowalne, a błędy są pokazywane na bieżąco i blokują podgląd. Dwa stałe hinty
nad produktami nadal wyjaśniają rabat i sposób wpisania samodzielnej zniżki.
Backend nadal waliduje pozycje niezależnie od kategorii; nie dodano mu reguły
„Zniżka” → ilość 1 i ujemna cena. Testy nie były uruchamiane ani aktualizowane;
istniejące asercje starego komunikatu dodatniego rabatu wymagają dostosowania.

**Transakcja innego płatnika w Expense — przegląd 2026-11-17:** formularz
rozpoznaje powiązanie przez `bankTransactionId`, a nie sukces pobrania szczegółów.
Gdy płatnik jest innym użytkownikiem, edycja i szczegóły pokazują, że transakcja
jest dołączona, oraz wskazują właściciela. Frontend nie pobiera wtedy jej
prywatnych szczegółów. W edycji nie ma przycisków zmiany/odłączenia transakcji,
płatnik i kwota są zablokowane. Hint płatnika brzmi „Płatnik jest właścicielem
dołączonej transakcji bankowej.” Formularz zachowuje kwotę z Expense jako
zastępczą wartość, ID trafia do preview/zapisu, a OCR nie nadpisuje kwoty
powiązanego wydatku. Właściciela wyznacza płatnik, nie autor: Expense nie ma
osobnego pola autora. Właściciel nadal może zmienić lub odłączyć transakcję.
Brak nowych testów HTTP ani potwierdzenia zapisu end-to-end jako drugi użytkownik.

**Komunikaty Banking — przegląd 2026-11-17:** `refreshErrorText` mapuje kody
refreshu i wyników kont na polskie opisy; nieznany kod ma ogólny komunikat,
brak kodu daje „Brak”. Sekcja jest opisana jako „Błąd”, bez surowego kodu.
`store.tsx` po zalogowaniu automatycznie pobiera connections, consents i accounts
w `Promise.all`; niepowodzenie ustawia wspólny `bankingError`. Osobny błąd
rozpoczynania połączenia mógł wcześniej powielić ten alert. BankingPage scala
identyczne komunikaty, a zagnieżdżone listy i odświeżanie pomijają już pokazany
alert. Usunięto podkreślony przycisk „Spróbuj ponownie” ze wspólnego alertu.
`BANKING_TINK_UNAVAILABLE` jest kodem zbiorczym z BankRefreshProcessor:
obejmuje problemy sieci, timeout, inne błędy dostawcy, ale też lokalne
`continuous_access_unverified` z TinkDataGateway. HTTP 401/403 jest mapowane
na ACCESS_DENIED, a `invalid_response` na INVALID_DATA. Sam kod UNAVAILABLE
nie dowodzi awarii Tink; przyczyna konkretnego zdarzenia pozostaje nieustalona.
Nie weryfikowano rzeczywistego Tink ani konfiguracji uruchomionych usług.

**Wspólna wysokość logowania i rejestracji — 2026-11-13:** SplitAuthLayout
rezerwuje jednakową minimalną wysokość części formularzowej dla obu ekranów,
zależną od szerokości okna. Układ obok siebie zaczyna się od 768px i zajmuje pełną
szerokość okna. Oba formularze stosują jednakową skalę zależną od wysokości okna,
aktualizowaną po resize, minimalnie 80%. Wspólne minimum strony to 35.5rem
(568px przy domyślnej wielkości tekstu); niższe okna przewijają całą stronę.
Skalowanie używa CSS zoom, bez transformacji pozostawiającej pustą przestrzeń.
Build TypeScript/Vite
zaliczony; pozostaje ostrzeżenie wielkości pakietu HEIC. Kontrola wizualna nie
została wykonana: narzędzie nie uzyskało zgody na dostęp do Chrome.

**Przewijanie powiadomień — 2026-11-13:** widok inboxa zajmuje dostępną wysokość
obszaru strony. Siatka CSS przydziela liście pozostałe miejsce po nagłówku,
filtrach, paginacji i opisie, bez stałej wysokości w pikselach. Lista ma własny
scroll i wraca na początek przy zmianie filtra lub strony. TypeScript/Vite build
zaliczony; bez kontroli wizualnej w przeglądarce i bez testów backendu.

**Testy frontendu — weryfikacja 2026-11-13:** wspólna atrapa `window` w testach
smoke udostępnia `matchMedia` używane przez responsywne menu. Każdy scenariusz
renderowania ustawia również własne `localStorage`, bez zależności od poprzedniego
testu. `npm.cmd test`: 17 zaliczonych, 0 błędów, 0 pominiętych. To testy Node/Vite
i renderowania serwerowego; nie potwierdzają wyglądu ani interakcji w przeglądarce.

**Ekran logowania i klient Google — weryfikacja 2026-11-12:** osobny widok Login
ma responsywny podział około 60/40: granatowy panel marki z logo i sloganem
„Przejmij kontrolę nad swoimi wydatkami” oraz jasny formularz z opcją Google,
podglądem hasła i odnośnikami do rejestracji/resetu. Rejestracja korzysta z tego
samego panelu marki, formularza w dwóch kolumnach na większych ekranach oraz
przycisku rejestracji Google. Odzyskiwanie hasła otwiera modal nad logowaniem,
również przy wejściu przez trasę forgot.
Formularz rezerwuje miejsce na błędy pól, API i komunikaty Google od pierwszego
renderowania, więc ich pojawienie nie przesuwa pól ani nie zmienia skali.
Aktualne wspólne wysokości i odstępy opisuje wpis z 2026-11-13 powyżej.
Pozostałe strony Auth zachowują
dotychczasowy układ. GoogleSignIn pobiera challenge/ClientId/nonce z istniejącego
API, ładuje oficjalny GIS SDK i wysyła credential do Google login. Rejestracja
wymagana przez API otwiera formularz loginu, imienia, nazwiska i daty urodzenia;
korzysta z Google register i obsługuje wymóg potwierdzenia e-maila. ID token nie
jest zapisywany w storage klienta ani wyświetlany. Błędy konfiguracji/skryptu
nie blokują logowania hasłem. Wymagane są włączone opcje Google i dozwolony origin.
Build TypeScript/Vite zaliczony (istniejące ostrzeżenie rozmiaru pakietu HEIC).
Nie uruchamiano testów frontendu ani rzeczywistego OAuth, rejestracji, SMTP czy
nowej kontroli wizualnej w przeglądarce. Integracja pozostaje do sprawdzenia
z rzeczywistą konfiguracją Google; sam podłączony klient nie potwierdza jej działania.

**Szczegóły grupy i nazwy członków — weryfikacja 2026-11-12:** frontend ma wspólne
oznaczenia roli (administrator niebieski, członek jaśniejszy) oraz członkostwa
(aktywne zielone, byłe czerwone) w informacjach, liście i szczegółach członka oraz
liście grup. Ujednolicono teksty opuszczania grupy, przekazania roli, archiwizacji,
rozliczeń i zaproszeń („kod” w UI, „Aktywne” zamiast „Można użyć”). Pusta lista
propozycji rozliczeń nie jest już pokazywana jako rozliczona w trakcie wczytywania.
`GroupMemberResponse` listy i szczegółów zawiera opcjonalne `displayName`.
GroupMemberService pobiera nazwy przez istniejący IUserDisplayNameLookup po
autoryzacji, jednym zapytaniem na stronę, także dla byłych członków. Frontend
korzysta z tych nazw zamiast ID; szczegóły członka pokazują również rolę.
Migracja nie jest potrzebna. Grouping.Tests: 46/46, w tym kontrola braku lookupu
przy odmowie dostępu oraz grupowe pobranie nazw. Kontrola typów TypeScript
zaliczona. Nie uruchamiano testów frontendu ani PostgreSQL.
API oraz projekty Banking.IntegrationTests i Expense.IntegrationTests kompilują
się w Release bez błędów i ostrzeżeń; konstrukcje serwisu w fixture'ach uwzględniają
lookup nazw. Te kompilacje nie oznaczają wykonania testów integracyjnych.

**Wspólne przetwarzanie ikon User/Grouping — weryfikacja 2026-11-12:** obsługa
PNG/JPEG/WebP/HEIC/HEIF, limitu 5 MiB, wymiarów, orientacji EXIF, skalowania do
512 × 512, usuwania metadanych i kodowania WebP została wydzielona do
`Shared.Images.IconImageProcessor`. Magick.NET 15.1.0 jest zależnością Shared.
UserIconService i GroupIconService mapują błędy na własny kontrakt i zachowują
własne prefiksy kluczy oraz obsługę storage. Nieudany upload grupy sprząta
wygenerowany klucz, tak jak User. Presety i istniejące obiekty są bez zmian.
Frontend korzysta ze wspólnego `src/shared/iconImage.ts` zamiast `profileImage.ts`;
obie kontrolki przyjmują oryginał do 25 MiB, konwertują HEIC lokalnie i wysyłają
zmniejszony obraz. Formularz grupy blokuje zapis podczas przygotowywania pliku.
User.UnitTests 72/72 i Grouping.Tests 41/41 zaliczone bez pominięć, w tym
rzeczywiste dekodowanie syntetycznego HEIC dla obu modułów i błąd uploadu grupy.
Kontrola typów TypeScript zaliczona. Nie uruchamiano testów frontendu, PostgreSQL,
przeglądarki ani rzeczywistego S3.

**Sortowanie listy grup — weryfikacja 2026-11-12:** `GET /api/groups` przyjmuje
`sortBy=name|joinedAt` (domyślnie `name`) i dotychczasowe `descending`.
Data to `JoinedAt` członkostwa zalogowanego użytkownika; sortowanie jest wykonywane
przed paginacją, z rosnącym ID grupy jako rozstrzygnięciem remisów. Nie zmieniono
filtrów członkostwa ani wykluczania archiwum. Frontend udostępnia nazwę A–Z/Z–A
oraz datę dołączenia rosnąco/malejąco. Nie jest potrzebna migracja.
33 testy Grouping zaliczone; kontrola typów TypeScript zaliczona. Testów frontendu
nie uruchamiano. Dodano test PostgreSQL kolejności, remisów, filtrowania i stron;
nie wykonano go, ponieważ uruchomienie jednorazowej bazy nie zostało zatwierdzone.

**Normalizacja zdjęć profilu i HEIC — weryfikacja 2026-11-12:** frontend przyjmuje
PNG/JPEG/WebP/HEIC/HEIF do 25 MiB i przygotowuje plik do maksymalnie 512 × 512 px
z zachowaniem proporcji i bez powiększania. Konwersja HEIC działa lokalnie przez
ładowane na żądanie heic-to/csp 1.6.7; zapis jest blokowany podczas przygotowania.
Backend nadal przyjmuje plik do 5 MiB (multipart 6 MiB), także HEIC/HEIF. Magick.NET
15.1.0 dekoduje wyłącznie dozwolony format i pierwszą klatkę, kontroluje wymiary
(16384 px na bok, 64 mln pikseli), stosuje orientację EXIF, zmniejsza do 512 × 512,
usuwa metadane i zapisuje WebP. Uszkodzone obrazy zwracają 415 przed uploadem.
Presety i istniejące zdjęcia pozostają bez przetwarzania wstecznego.
User.UnitTests: 72/72, User.Tests (HTTP): 101/101, bez pominięć. Testy sprawdzają
rzeczywiste dekodowanie PNG/JPEG/WebP/HEIC (syntetyczna próbka HEIC), skalowanie,
orientację, usunięcie metadanych, przezroczystość, limity i błędy przed storage.
Build TypeScript/Vite zaliczony; osobny pakiet HEIC ma ostrzeżenie o rozmiarze
3,2 MB przed kompresją i ładuje się dopiero przy wyborze HEIC. Zgodnie z życzeniem
użytkownika nie uruchamiano testów frontendu. Bez testu w przeglądarce, rzeczywistego
S3 ani nowego przebiegu testów PostgreSQL; ich projekt kompiluje się w Release
bez błędów i ostrzeżeń. Zaktualizowano ich fixture PNG, bo sam
nagłówek pliku nie przechodzi już pełnego dekodowania. Kontrakt:
[development](development.md#zdjęcia-profilu-i-prefiksy-storage).

**Pozycjonowanie okien frontendowych — przegląd kodu 2026-11-12:** wspólny
`Modal` renderuje warstwę przez portal do `document.body`, poza przewijaną
i animowaną zawartością widoków. Okna są centrowane względem widocznego ekranu
na wszystkich szerokościach; wysokość ogranicza `100dvh` z marginesem, a długa
treść przewija się wewnątrz okna. Na prośbę użytkownika nie uruchamiano testów
frontendu; nie wykonywano nowej weryfikacji w przeglądarce.

**Frontend konta i identyfikacja karty — weryfikacja 2026-11-12:** tytuł karty
to `4Money`, a favicon korzysta z istniejącego logo aplikacji. Profil i menu
boczne pokazują zdjęcie pobierane przez podpisany URL lub inicjały przy braku
zdjęcia/błędzie obrazu. URL jest odświeżany przed wygaśnięciem. Osobny dialog
pozwala wgrać PNG/JPG/WebP do 5 MiB, wybrać preset z API lub wrócić do inicjałów
(`removeIcon`). Pusta lista presetów pokazuje zapowiedź ich dostępności;
usunięto lokalne, niezgodne z kontraktem identyfikatory ikon Lucide.
Edycja pozostałych danych profilu zachowuje zdjęcie.
Sekcja hasła wysyła żądanie `forgot-password` dla adresu konta i prowadzi
do istniejącego ustawiania/resetu hasła przez e-mail; konto bez hasła ma opis
„Ustaw hasło”. Nie dodano zmiany hasła przez podanie aktualnego hasła.
Build TypeScript/Vite i 17 testów Node zaliczone, bez pominięć. Nowe testy
obejmują kontrakty JSON/multipart ikon, odczyt presetów/URL oraz renderowanie
inicjałów i ustawień profilu. Brak weryfikacji interakcji i wyglądu w przeglądarce
(narzędzie nie otrzymało dostępu do Chrome), rzeczywistego SMTP i storage.

**Frontend paragonów i OCR — weryfikacja 2026-11-11:** usunięto wywołania
nieistniejących endpointów osobnego uploadu, odczytu, analizy po ID i usuwania
paragonu. Wybrany plik i podgląd pozostają lokalne; analiza wysyła multipart
`file` do `POST /api/expenses/receipts/analyze`. Zapis POST/PUT Expense wysyła
razem `expense` (JSON), `file` i opcjonalny `ocrProof`. Nieudany zapis zachowuje
plik i dowód do ponowienia, a odrzucenie pliku zwalnia lokalny podgląd.
Odczyt zapisanego paragonu korzysta z `GET /api/expenses/{expenseId}/receipt`
i pola `analysis`. Odpięcie odbywa się przez `removeReceipt` przy edycji Expense.
Mapowanie OCR obsługuje datę `YYYY-MM-DD` i obiekty ostrzeżeń; formularz nie wymaga
godziny, której kontrakt Expense nie zapisuje. Preview edycji przekazuje expenseId i version.
Build TypeScript/Vite i 12 testów Node zaliczone, bez pominięć. Testy obejmują
format żądań, mapowanie odpowiedzi i lokalny cykl pliku z ponowieniem zapisu.
Nie wykonywano rzeczywistego OCR, uploadu do storage ani testu interakcji
w przeglądarce; konfiguracja działających usług zewnętrznych pozostaje wymagana.

**Typ pliku paragonu — 2026-11-16:** analiza i zapis uzupełniają MIME na podstawie
obsługiwanego rozszerzenia, gdy typ przeglądarki jest pusty lub wynosi
`application/octet-stream`. Test multipart obejmuje wszystkie rozszerzenia
i sprawdza zachowanie bajtów. Dekoder w lokalnym workerze przyjął syntetyczne
JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP i PDF.

**Import kwot z OCR — 2026-11-16:** Expense używa ujemnego `total` samodzielnej
zniżki, gdy OCR nie zwrócił ceny i ilości. Wpisy z `deposits` i `deposit_returns`
trafiają do propozycji jako osobne pozycje z kategorią „Inne”. Gdy odczytana suma
wiersza kaucji różni się od ceny razy ilość, propozycja stosuje odczytaną sumę
i oznacza wiersz do sprawdzenia. `amount_due` pozostaje kwotą całego wydatku.
Wynik OCR przy dodawaniu i edycji pokazuje rabat, sumę wiersza i oznaczenia do
sprawdzenia. Reguły rozpoznawania podsumowań rabatów i duplikatów pozostają po
stronie workera. Zaliczone: 3 wybrane testy integracyjne Expense na tymczasowym
PostgreSQL, 48 testów frontendu i build TypeScript/Vite. Test integracyjny używa
syntetycznej odpowiedzi workera.

**Katalog kategorii — 2026-11-16:** migracja
`20261116211631_NormalizeExpenseCategoriesAndAddDeposit` zmienia pierwszą literę
19 nazw i dodaje kategorię „Kaucja” z ikoną Lucide `recycle`. Aktualizuje dane
katalogu po istniejących ID. Frontend rozpoznaje tę ikonę w etykietach kategorii.
W lokalnej bazie `zpi4m_frontend_demo` zastosowano migrację. W `zpi4m_db` ze
starszą historią EF dodano tylko wiersz „Kaucja” poza migracjami, ponieważ
dotychczasowe nazwy tej bazy zaczynały się już wielką literą.

**Kategorie korekt OCR i ikony — 2026-11-16:** Expense przypisuje wpisy
z `deposits` i `deposit_returns` do „Kaucji”. Pozycje z `items`, w tym opłaty,
zachowują kategorię zwróconą przez worker w polu `category`.
Frontend obsługuje nazwy Lucide z całego katalogu. Zwykłe kategorie korzystają
ze wspólnego stylu miętowego, systemowe „Nieznane” ma styl neutralny.

**Reset i pierwsze hasło User — weryfikacja 2026-11-11:** `PasswordSetup`
korzysta ze wspólnego szablonu HTML i linku `?action=reset#token=...`.
`PasswordResetService` wysyła SMTP po SaveChanges, przed commitem; awaria
lub anulowanie wysyłki wycofuje nowy token i zachowuje poprzedni oraz jego daty.
Odpowiedź forgot-password przy awarii SMTP pozostaje 202.
Testy User Release: UnitTests 64/64, Tests (HTTP) 101/101,
IntegrationTests 89/89 na jednorazowym PostgreSQL 15, bez błędów i pominięć.
Obejmuje trzy wcześniej nieudane scenariusze i dwa nowe przypadki anulowania
SMTP przy resecie z poprzednim tokenem i bez niego. Kompilacja tych projektów
bez zgłoszonych ostrzeżeń; kontrola whitespace zmienionych plików C# zaliczona.
Nie uruchamiano ponownie całej solucji ani rzeczywistej wysyłki SMTP.
Kontener PostgreSQL i jego wolumeny usunięto po testach.
Ujednolicono opisy kolejności SMTP i commitu z zachowaniem wyjątku rejestracji
Google wymagającej potwierdzenia adresu, która wysyła SMTP po commicie.

**Linki mailowe User — weryfikacja 2026-11-04:** wiadomości HTML z przyciskiem
obsługują potwierdzenie rejestracji, zmianę e-maila, reset hasła i dezaktywację.
Frontend pobiera token z fragmentu URL: potwierdzenia wykonuje automatycznie,
reset wymaga nowego hasła. Usunięto testowe linki. Formularze przypominają o spamie.
W tym wdrożeniu adres ustawiał UserEmailLinks:FrontendBaseUrl; obecnie host
wymaga FRONTEND_URL (patrz bieżący przegląd i development.md).
Zwykła rejestracja i wystawianie nowych tokenów są zatwierdzane po przyjęciu
przez SMTP; awaria wycofuje zapis i zachowuje poprzednie tokeny. Wyjątkiem jest
rejestracja Google wymagająca potwierdzenia adresu: SMTP działa po commicie,
a awaria zachowuje konto, grupę i token do ponownej wysyłki.
Ograniczenia SMTP/bazy opisuje architektura.
Build API Release: zero błędów/ostrzeżeń. User: 29 testów jednostkowych,
64 HTTP i 37 integracyjnych PostgreSQL zaliczone, bez pominięć. Frontend:
build TypeScript/Vite i 5 testów Node zaliczone. Nie wysyłano rzeczywistych
wiadomości ani nie potwierdzono dostarczenia do skrzynki.

Przegląd User, testów, AGENTS.md, agent-context i opisów Postmana wykrył
pozostałości łączenia gałęzi. Naprawiono sygnaturę fixture User z callbackiem
konfiguracji usług, wywołania konstruktora Receipt w testach Expense oraz
test odczytu URL paragonu. Testy migracji wskazują właściwe migracje po nazwie,
zamiast zakładać usuniętą tymczasową Expense albo ostatnią pozycję Banking.
Designer UserImprovements zsynchronizowano z bieżącym snapshotem EF: zachowuje
pełny model Expense i BankTransactionAssignment dołączony przez master.
SQL Up/Down migracji User nie zmieniano.

UploadForExpenseAsync używa teraz RECEIPT_STORAGE_PREFIX tak jak CreateAsync;
test sprawdza oba uploady i odczyt historycznego klucza. Oba szablony env
domyślnie wyłączają opcjonalne Google, aby pusty ClientId nie blokował startu.
Poprawiono nieaktualne opisy modelu, migracji, konfiguracji i żądań Postmana.

Release build całej solucji: 0 błędów i ostrzeżeń. Końcowe wyniki 15 projektów:
1028 zaliczonych, 0 błędów, 1 pominięty ręczny test rzeczywistego S3.
User: UnitTests 52/52, Tests 101/101, IntegrationTests 81/81;
Banking.IntegrationTests 151/151, Expense.IntegrationTests 110/110,
Notifications.IntegrationTests 13/13, Receipt.UnitTests 108/108.
Testy User potwierdzają lokalne logowanie, Google, pierwsze hasło, sesje,
zdjęcia profilu, grupę startową oraz migracje na prawdziwym PostgreSQL 15.
Google, SMTP, OCR i storage w testach przepływu są zastępowane atrapami.
Nie potwierdza to rzeczywistych usług zewnętrznych ani integracji frontendu.

Pierwszy przebieg ograniczał sandbox Windows; uruchomienie poza nim usunęło
błędy dostępu hostów testowych do dziennika zdarzeń. Równoległa regresja
zgłosiła też timeouty bazy. Cztery projekty PostgreSQL uruchomiono ponownie
kolejno, z maksymalnie dwoma wątkami xUnit; wszystkie przeszły bez pominięć.
Kontrole stylu i whitespace zmienionych plików C# oraz git diff --check przeszły.
Postman: poprawny JSON 8 plików, składnia 214 skryptów, 42 skrypty User
wykonane na syntetycznych danych; 73 istniejące testy skryptów Banking przeszły.
Sprawdzono istnienie lokalnych odnośników w AGENTS.md, agent-context i README
Postmana. Raporty TRX i logi: `.cache/backend-results/rebase-20261110-final/`.
Jednorazowy kontener PostgreSQL i jego anonimowy wolumen usunięto po testach;
nie pozostały procesy testhost. Nie migrowano ani seedowano bazy aplikacji.
Starsze wyniki poniżej dotyczą wcześniejszych rewizji i przebiegów.
## Expense — dedykowane agregacje dla Reports, 2026-11-17

Refaktoryzacja repozytoriów 2026-11-18: ExpenseAggregationService w
Services/Aggregation korzysta z IExpenseRepository.GetAggregatedExpenseTotalsAsync.
Implementacja oraz typy ExpenseAggregateRow i ExpenseAggregationType są w
Repositories/ExpenseRepository; SQL pozostaje w osobnym ExpenseAggregationQueries.cs.
Serwis używa istniejącej rejestracji scoped IExpenseRepository w hoście.
Bez zmian logiki, SQL, parametrów, wyników serwisu ani endpointów HTTP.

Usunięcie nieużywanego odczytu 2026-11-18: poza DI i testami starszy serwis nie
miał konsumentów. Usunięto jego interfejs, implementację, kalkulator w pamięci,
dziesięć prywatnych DTO, dwa odczyty repozytorium, projekcję encji z udziałami
i opcję włącznego końca historii. Bieżąca historia zawsze miała tę opcję wyłączoną.
Test finansowy agregacji SQL zachowano z jawnymi oczekiwanymi kwotami dla tych
samych zakupów; usunięto testy dotyczące wyłącznie starego odczytu i kalkulatora.
Weryfikacja: cała solucja Release bez błędów i ostrzeżeń, Expense.UnitTests 67/67,
Reports.UnitTests 129/129, bez pominięć; `git diff --check` zaliczony.
Spadek liczby testów Expense o siedem wynika wyłącznie z usunięcia testów starego
kalkulatora. Testy integracyjne skompilowano, ale ich nie wykonywano.
Nie uruchamiano bazy ani Dockera. Reports i aktywne serwisy agregacji pozostały
bez zmian; SQL zmienił wyłącznie komentarz odwołujący się do usuniętego kalkulatora.

Ujednolicenie nazewnictwa 2026-11-18: kontrakty są w Services/Contracts/Aggregation,
typy otrzymały prefiks ExpenseAggregation. Starszy kalkulator i odczyty encji
były częścią tej zmiany nazw, a następnie zostały usunięte jako nieużywane.
Zaktualizowano konsumentów, DI, testy i dokumentację bez zmian obliczeń ani SQL.
Weryfikacja po zmianie nazw: cała solucja Release bez błędów i ostrzeżeń,
Expense.UnitTests 74/74, Reports.UnitTests 129/129, bez pominięć; `git diff --check`
zaliczony. Nie uruchamiano testów integracyjnych ani PostgreSQL/Dockera.

Weryfikacja 2026-11-18 po przeniesieniu agregacji: kompilacja całej solucji Release
z `--no-restore --disable-build-servers -m:1` bez błędów i ostrzeżeń.
Expense.UnitTests 74/74 i Reports.UnitTests 129/129, bez pominięć.
Porównanie ze stanem wyjściowym potwierdziło zachowanie CTE, końcowego SQL,
wyboru agregacji, parametrów zapytania i logiki serwisu.
Brak odwołań do usuniętych typów repozytorium; `git diff --check` zaliczony.
Testy integracyjne dostosowano i skompilowano, ale ich nie uruchamiano.
Nie uruchamiano ani nie konfigurowano PostgreSQL, Dockera ani innych usług zewnętrznych.

Późniejsza ocena LINQ i weryfikacja 2026-11-18: zachowano powyższą implementację
SQL. Wariant IQueryable ze skorelowanymi Count/Sum przetłumaczył się i dał zgodne
wyniki na syntetycznym zbiorze dla wszystkich sześciu agregacji w obu zakresach,
ale osobiste kategorie po rozgrzaniu trwały 471–487 ms zamiast 50–52 ms.
EXPLAIN ANALYZE wykazał powtarzaną agregację pozycji w korelacjach. Nie wdrożono
wariantu pogarszającego wydajność. [Analiza i ograniczenia](../notes/EXPENSE_LINQ_EVALUATION.md).
Na osobnym tymczasowym PostgreSQL 15 wykonano Expense.IntegrationTests 128/128,
bez pominięć; fixture tworzył i usuwał wyłącznie własne losowe bazy.
Expense.UnitTests 74/74, Reports.UnitTests 129/129, bez pominięć.
Kompilacja całej solucji Release bez błędów i ostrzeżeń; `git diff --check` zaliczony.
Testy integracyjne potwierdzają pozostawioną metodę SQL, nie pełną zgodność prototypu LINQ.
Bez zmian kodu aplikacji i oczekiwanych wyników testów w ramach tej oceny.

ExpenseAggregationService implementuje cztery metody IExpenseAggregationService:
kategorie, okresy Month/Quarter/Year z kategoriami, dni tygodnia i konkretne daty.
ExpenseRepository wykonuje wspólny parametryzowany SQL z CTE i funkcjami
okienkowymi. Filtrowanie ExpenseItem, dwa etapy rozdzielania groszy i końcowe
sumowanie odbywają się w PostgreSQL; aplikacja pobiera tylko agregaty.
Personal zachowuje koszt z udziału, Group pełne kwoty dostępnych zakupów.
Pusta lista kategorii zwraca pustą listę; nie dodajemy pustych dni/okresów ani
„Pozostałych”. Dostęp zachowuje istniejące reguły historii Expense i publiczny
lookup aktywnych grup Grouping. Serwis odrzuca niepoprawne argumenty wewnętrzne,
ale walidacja requestów pozostaje w Reports. Scoped DI jest w AddExpenseServices.
Bez migracji i nowych endpointów. Reports korzysta obecnie z IExpenseAggregationService;
stan jego dostosowania opisano poniżej. Szczegóły: [kontrakt](../notes/EXPENSE_REPORTS.md).

Weryfikacja 2026-11-17: pełny Expense.IntegrationTests z PostgreSQL 18:
127/127 bez pominięć (17 nowych przypadków), Expense.UnitTests 74/74.
Release build całej solucji bez błędów i ostrzeżeń. Po dopracowaniu obsługi
timestampów infinity ponowiono 17/17 nowych przypadków w Release, bez pominięć.
Testy porównują grosze SQL z istniejącymi kalkulatorami dla obu uczestników,
obejmują filtry, okresy częściowe, udziały, brak dostępu, dni tygodnia, rzadkie
daty, zerowe/ujemne kwoty i lata 1/9999 (także domyślne mapowanie Npgsql na infinity).
Whitespace i git diff --check zaliczone. Na Windows provider EventLog wyłączono
zmienną procesu testowego; serwer testowy uruchomiono poza sandboxem ze względu
na blokowanie sygnałów między procesami. Bazy aplikacji nie zmieniano.
Nie wykonywano testów obciążeniowych ani regresji testów innych modułów.

## Reports — stan 2026-11-18

Końcowa weryfikacja 2026-11-18: Expense.UnitTests 67/67,
Expense.IntegrationTests 122/122, Reports.UnitTests 182/182 i
Reports.IntegrationTests 19/19, bez błędów i pominięć. Pełny host sprawdził
również zakres 1970–2060 oraz produkcyjne OpenAPI. Szczegóły i wyniki
regresji całej solucji: [raport](../notes/REPORTS_FINAL_VERIFICATION.md).
Usunięto nieużywany ExpenseAggregationParameters, bez zmiany aktywnych kontraktów.
Końcowa regresja wszystkich 17 projektów z PostgreSQL 15 i testem S3:
1277 zaliczonych, 0 błędów, 0 pominiętych. Release bez błędów i ostrzeżeń;
`git diff --check` poprawny. Zasoby testowe usunięto, bez zmian danych aplikacji.

Weryfikacja 2026-11-18 po zmianie wspólnej walidacji dat: cała solucja Release
bez błędów i ostrzeżeń, Reports.UnitTests 182/182 (w tym HTTP/OpenAPI czterech
endpointów), Expense.UnitTests 67/67, bez pominięć. Testy obejmują minimum
1970-01-01, wcześniejsze daty, zakresy wieloletnie, odwrócony zakres i granice
typu DateOnly. Zaktualizowano OpenAPI, kontrakt Reports i opis kolekcji Postman;
JSON kolekcji i zapisanych odpowiedzi oraz `git diff --check` poprawne.
Testy integracyjne dostosowano i skompilowano, ale ich nie wykonywano.
Nie uruchamiano PostgreSQL ani Dockera.

Weryfikacja 2026-11-18 po uproszczeniu wiązania `categoryIds`: cała solucja
Release bez błędów i ostrzeżeń, Reports.UnitTests 133/133 (osobno HTTP/OpenAPI
109/109), Expense.UnitTests 67/67, bez pominięć. Expense uruchomiono ponownie
z `Logging__EventLog__LogLevel__Default=None` po odmowie dostępu do Windows EventLog.
Testy integracyjne dostosowano i skompilowano, ale ich nie wykonywano.
Nie uruchamiano PostgreSQL, Dockera ani innych usług zewnętrznych.

IReportsService i ReportsService obsługują GetCategoryBreakdownAsync,
GetExpenseTotalsByPeriodAndCategoryAsync, GetExpenseTotalsByDayOfWeekAsync
i GetDailyExpenseTotalsAsync. Wszystkie requesty dziedziczą po ReportRequest:
DateFrom, DateTo, Scope, GroupId i domyślnie puste CategoryIds. Wspólne
ReportsService.Validate wymaga obu dat >= 1970-01-01 i DateFrom <= DateTo
(HTTP 400 przy naruszeniu). Nie ogranicza długości zakresu ani samego roku
i nie dodaje biznesowej górnej granicy dat; obowiązuje zakres DateOnly. Reports waliduje parametry,
przekazuje je bezpośrednio do IExpenseAggregationService, mapuje błędy przez
ReportsReadException i pobiera nazwy kategorii przez publiczny IExpenseService.
Odczyt/autoryzacja nadal korzystają z istniejącego przepływu Expense → Grouping;
Reports nie dodaje osobnego mechanizmu członkostwa ani zapytań do bazy.

Koło ma do 8 kategorii lub top 8 i „Pozostałe” (CategoryId=null), sortowane po
sumie malejąco. Serie okresowe używają tego samego top 8 z całego zakresu,
„Pozostałe” liczone per okres; brakujące okresy mają zera. Znane wybrane kategorie
bez zakupów również mają serię zerową. Progi agregacji liczą miesiące kalendarzowe
objęte zakresem, także niepełne: do 24 Month, do 48 Quarter, dalej Year.
Ranking dni tygodnia zwraca AverageAmount (decimal bez zaokrąglania do grosza),
dzieląc sumy przez liczbę wszystkich wystąpień danego dnia w zakresie, w kolejności
poniedziałek–niedziela. Dzień niewystępujący w krótkim zakresie ma średnią 0.
Kalendarz zwraca wszystkie daty zakresu z zerami. Puste CategoryIds daje pusty
wynik każdego raportu. Nie zmieniono implementacji Expense ani Grouping.

HTTP ma cztery docelowe trasy GET: category-breakdown, period-category-totals,
day-of-week-totals i daily-totals, bez aliasów starych tras. Standardowe wiązanie
ASP.NET Core obsługuje kolekcję Guid, także powtarzane i indeksowane parametry.
Brak `categoryIds` oznacza pustą listę i pusty raport (200), nigdy wszystkie kategorie.
`categoryIds=`, błędny UUID, mieszanie pustych wartości/UUID i kilka pustych wartości
dają 400 z model bindingu. Guid.Empty nadal odrzuca serwis. Nie ma własnego bindera
ani walidacji pustych wartości HTTP. Kontroler odrzuca liczbowe/nieznane scope, powtórzone
scope/daty/groupId i niepoprawny groupId. Warunki dat/scope/grupy waliduje Reports;
uprawnienia egzekwuje istniejący odczyt Expense/Grouping. Dedykowany transformer
OpenAPI hosta opisuje pięć wspólnych parametrów, opcjonalną tablicę UUID i progi okresów.
Jedna nowa kolekcja Reports w Postmanie zawiera 17 definicji GET i syntetycznych
odpowiedzi: personal/group, puste wybory, Pozostałe i progi 24/25/48/49 miesięcy.
Weryfikacja 2026-11-17: dostosowane Reports.UnitTests 129/129 i
Reports.IntegrationTests 18/18 w Release, bez pominięć, z PostgreSQL 15 w osobnym
tymczasowym kontenerze i losowymi bazami testowymi. Testy obejmują aktualne
filtry, puste/błędne/mieszane listy, progi 24/25/48/49 miesięcy, top 8/Pozostałe,
stałe serie i zera, średnie dla dni bez wydatków, skrajne daty, przełom roku,
grosze, ujemne kwoty, odmowę dostępu i historię byłego członka.

ReportsWorkflowTests używa rzeczywistych agregacji Expense i repozytoriów
Grouping/kategorii; tylko auth i granica metadanych kategorii mają adaptery
testowe. ReportsProductionHostTests dodatkowo uruchamia pełny host Kestrel
z produkcyjnym DI i rzeczywistym IExpenseService, logowaniem seed_anna oraz JWT.
Wykonano wszystkie 17 definicji GET z kolekcji Postman przez HttpClient
(nie Postman CLI), wygenerowano OpenAPI i potwierdzono odrzucenie sesji po logout.
Klucze są losowe i procesowe; test nie czyta lokalnego .env, nie ujawnia tokenów
i nie wywołuje S3/OCR/SMTP/Tink. Każdy host i jego baza są sprzątane po teście.
Nie zmieniono logiki produkcyjnej w ramach tej weryfikacji. Wcześniejsze wyniki
poniżej dotyczą historycznego ReportsService.

Końcowa regresja całej solucji 2026-11-17: 17 projektów, 1071 zaliczonych,
0 błędów, 1 pominięty ręcznie włączany test Receipt S3 (STORAGE_INTEGRATION_TESTS=0).
Wszystkie testy PostgreSQL wykonano na osobnym Docker PostgreSQL 15 z bazami
fixture, bez używania bazy aplikacji. Końcowy Release build solucji: 0 błędów
i ostrzeżeń; whitespace i git diff --check zaliczone. Tymczasowy kontener
PostgreSQL oraz procesy hostów testowych zostały zatrzymane. Nie wykonywano
testów obciążeniowych ani rzeczywistych połączeń Tink/SMTP/S3/OCR.

## Reports — stan 2026-11-16

Poniższy opis i wyniki są historyczne; aktualny kontrakt opisano powyżej.

Reports zawiera DTO czterech wykresów oraz interfejs IReportsService w
Services/Reports: GetCategoryBreakdownAsync, GetMonthlyTotalsAsync,
GetSpendingTrendAsync i GetDailyTotalsAsync. Metody przyjmują actor pochodzący
z ICurrentUser, request i CancellationToken oraz zwracają asynchronicznie
odpowiedni Response. Brak GroupId oznacza koszty z udziałów użytkownika,
a podane GroupId pełne koszty wskazanej grupy.

Historyczny ReportsService implementował cztery raporty przez dawny odczyt Expense, bez
bezpośredniego dostępu do encji ani repozytoriów Expense. Wybiera UserShareAmount
dla raportu osobistego i Amount dla grupowego. Listy kategorii filtruje po ID
na agregatach kategorii, zachowując obliczenia groszy Expense. Sumy miesięczne
zwracają 12 pozycji, dzienne każdy dzień miesiąca, a trend wspólne przedziały
chronologiczne z zerami dla pustych okresów. Serie trendu obejmują kategorie
obecne w odczycie dla wybranego zakresu; całkowity brak danych daje pustą listę
serii. Kategorie o ujemnej sumie zachowują znak, bez reguł prezentacji wykresu.

Grupowanie zatwierdzone przez użytkownika 2026-11-17: trend używa dni do 31 dni włącznie,
tygodni do 366 dni i miesięcy powyżej 366 dni. Tydzień zaczyna się w poniedziałek;
PeriodStart wskazuje początek kalendarzowego przedziału, także przed From.
Skrajne przedziały sumują tylko daty objęte filtrem. Tygodnie są sumowane
z dziennych danych Expense, bez zmiany jego kontraktu.

Walidacja serwisu odrzuca odwrócone zakresy, lata poza 1–9999, miesiące poza
1–12 i puste identyfikatory. Metody zwracają bezpośrednio Response, a błędy
odczytu przekazują przez ReportsReadException, zachowując ExpenseErrorCode
i ValidationErrors ze źródła. Lokalna walidacja rzuca ValidationException
z ValidationResult wskazującym pola pierwszego błędu. Nie ma ReportsResult,
osobnego enumu błędów Reports ani katalogu Results/Contracts w Reports.
Anulowanie nadal przerywa operację przez OperationCanceledException.
Odmowa dostępu nie jest zamieniana na puste dane. CancellationToken jest
przekazywany do odczytów. Host rejestruje scoped IReportsService → ReportsService
i kontroler Reports. Cztery autoryzowane endpointy GET w /api/reports obsługują
raport kategorii, miesięczne sumy, trend i dzienne sumy. Kontroler pobiera ID
przez ICurrentUser, zamienia wyjątki na ProblemDetails i wyłącza cache.
Wymagane parametry GET mają BindRequired, daty wymagają YYYY-MM-DD,
a rok zakresu 1–9999. Kontrakt: [Reports API](../notes/REPORTS_API.md).
Projekt ZPI4M.Reports.UnitTests jest dodany do solucji; jego testy używają
atrapy ówczesnego serwisu odczytu Expense, bez bazy i usług zewnętrznych. Testy HTTP TestServer
używają rzeczywistego ReportsService i HttpCurrentUser oraz testowego
uwierzytelniania; nie potwierdzają walidacji JWT ani dostępu na rzeczywistej bazie.

Weryfikacja HTTP 2026-11-16: Reports.UnitTests 55/55, bez pominięć (26 testów
serwisu i 29 przypadków HTTP). Build Release hosta API z zależnościami:
0 błędów i ostrzeżeń. Potwierdzono cztery trasy, brakujące i błędne parametry,
osobiste i grupowe kwoty, ID z uwierzytelnienia, serializację JSON, mapowanie
wyjątków i brak ujawniania komunikatów awarii transportu. Kontrole dotnet format
style/whitespace Reports i testów oraz git diff --check przeszły. Nie uruchamiano
regresji pozostałych modułów, PostgreSQL, JWT hosta ani usług zewnętrznych.

Weryfikacja wariantu z wyjątkami 2026-11-16: Reports.UnitTests 26/26,
bez pominięć, z kompilacją Release zależności i projektu testowego. Sprawdzono
dane czterech raportów, wyjątki odczytu z kodami i szczegółami źródła, pola
błędów walidacji bez odczytu Expense oraz anulowanie. Kontrole dotnet format
style, whitespace i git diff --check przeszły. Bez nowej regresji pozostałych
modułów, testów PostgreSQL i usług zewnętrznych.


Weryfikacja 2026-11-13: Release build zależności i projektu testowego zakończony
bez ostrzeżeń i błędów; Reports.UnitTests 23/23, bez pominięć. Kontrole
dotnet format style, whitespace nowych plików oraz git diff --check przeszły.
Nie uruchamiano regresji pozostałych modułów, PostgreSQL ani usług zewnętrznych.

Weryfikacja integracji Reports 2026-11-17: nowy projekt
ZPI4M.Reports.IntegrationTests jest w solucji i korzysta z linkowanego fixture
PostgreSQL Expense (osobna losowa baza na przypadek, SQL init, migracje, seed,
cleanup). 20/20 testów HTTP → ReportsService → dawny odczyt Expense → PostgreSQL
zaliczonych bez pominięć na jednorazowym PostgreSQL 15. TestServer używa
HttpCurrentUser i testowego uwierzytelniania; nie zastępuje ówczesnego serwisu odczytu Expense
ani repozytoriów. Nie potwierdza JWT i rejestracji pełnego hosta API.
Sprawdzono cztery raporty, osobiste i grupowe kwoty, filtry kategorii i dat,
puste okresy, cudze i nieistniejące grupy, byłego członka, płatnika bez udziału,
grosze, ujemne pozycje, lata 1/9999, rok przestępny i progi trendu.
Build Release całej solucji: 0 błędów i ostrzeżeń. Reports.UnitTests 55/55,
Expense.UnitTests 74/74 i Expense.IntegrationTests 110/110 na PostgreSQL,
bez pominięć. W testach Expense wyłączono provider Windows EventLog
zmienną procesu z powodu odmowy dostępu do dziennika .NET Runtime; bez zmiany kodu.
Nie zmieniano kodu produkcyjnego, kontraktów, agregacji ani zakresów dat.
Raportowanie limitów i optymalizacja agregacji pozostają do dyskusji zespołu.
Kontrola stylu nowych testów i git diff --check przeszły; formatowanie dopasowano
do backend/.editorconfig. Wyniki TRX: .cache/backend-results/reports-integration-20261117/.

## Expense i integracja Receipt — stan 2026-11-10

Działa 10 operacji opisanych w [kontrakcie Expense](../notes/EXPENSE_API.md):
kategorie, dostępne transakcje bankowe, historia, szczegóły, preview tworzenia
i edycji, POST, PUT, DELETE oraz analiza i odczyt paragonu. Kontrolery korzystają
z IExpenseService, który deleguje szczegóły, historię i kategorie do
ExpenseQueryService w Services/Reads. Bieżące raporty korzystają z IExpenseAggregationService.
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

Historyczny serwis odczytu (stan 2026-11-08, usunięty 2026-11-18) dostarczał Reports listę, sumy, podział na kategorie i oś
czasu dzień/miesiąc/rok, z autoryzacją i filtrami. Oddziela wartość zakupów,
koszt użytkownika i kwotę zapłaconą. Daty raportów są obustronnie włączne;
historia HTTP zachowuje koniec wyłączny. Kategorie kosztu użytkownika są
proporcjonalne do jego udziału, z uzgodnieniem groszy. Szczegóły i ograniczenia:
[kontrakt odczytu](../notes/EXPENSE_REPORTS.md). Nie dodano endpointów ani migracji.

Weryfikacja odczytów dla Reports (2026-11-08): Release build całej solucji
bez błędów i ostrzeżeń, Expense.UnitTests 73/73 oraz pełne
Expense.IntegrationTests 109/109 na PostgreSQL 15, bez pominięć.
Wcześniejszy przebieg nowych testów ówczesnego odczytu Expense: 6/6. Potwierdzono obie daty
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
Solucja ma 17 projektów testowych: po trzy User, Banking i Notifications,
po dwa Receipt, Expense i Reports oraz Grouping i Shared. Testy PostgreSQL wymagają
TEST_POSTGRES_CONNECTION_STRING i tworzą własne losowe bazy; brak konfiguracji
lokalnie oznacza pominięcie, a w CI błąd. Starsze wyniki poniżej zachowują daty
swoich weryfikacji.

## Narzędzia migracji — weryfikacja 2026-11-07

Przed dołączeniem Expense solucja miała trzynaście projektów testowych: po trzy
User, Banking i Notifications, dwa Receipt oraz Shared.UnitTests i Grouping.Tests.
Po dołączeniu Reports ma siedemnaście. Historyczne wyniki z poprzednich rewizji: 152 testy .NET
(User + Notifications, w tym 45 Notifications, PostgreSQL 17.6) oraz opisane
w kontekście Banking 223 testy .NET i 45 testów Pythona dla ówczesnego zakresu.
Nie sumuj tych wyników ani nie przedstawiaj ich jako przebiegu aktualnej solucji
po rebase, zmianie migracji i seeda. Aktualny zestaw wymaga ponownego uruchomienia.
CI dostarcza PostgreSQL 15 i jedną zmienną TEST_POSTGRES_CONNECTION_STRING
dla wszystkich trzech modułów. Ostatnie częściowe przebiegi opisano poniżej;
nie zastępują pełnego uruchomienia solucji z PostgreSQL.

## Scalona migracja UserImprovements — 2026-11-10

Zmiany unikalności aktywnego e-maila, triggera grupy startowej, tożsamości Google
i zdjęcia użytkownika są połączone w `20261109191600_UserImprovements`.
Osobne migracje z zakresu od unikalności e-maila do IconStorageKey usunięto;
designer pochodzi z końcowego modelu, snapshot pozostaje bez zmian.
Up wykonuje wszystkie cztery zmiany. Down najpierw blokuje tabelę i odrzuca
konta bez lokalnego hasła, a następnie cofa zmiany; powtórzone e-maile blokują
przywrócenie globalnego UNIQUE. Całość jednej migracji podlega transakcji EF.
Testy nie odwołują się już do usuniętych stanów pośrednich.

Historia EF bazy z zastosowanymi wcześniejszymi migracjami nie jest automatycznie
przepisywana. Dla lokalnej bazy przeznaczonej do odtworzenia użyj SQL init →
aktualne migracje → seed. Nie uruchamiano tych operacji na bazie aplikacji.
Weryfikacja po scaleniu: Release build API i User.IntegrationTests bez błędów
i ostrzeżeń; EF has-pending-model-changes potwierdza zgodność modelu.
Pięć testów migracji na jednorazowym PostgreSQL 15 zaliczonych: Up/Down/Up,
blokady cofania przy powtórzonych e-mailach i kontach bez haseł, brak backfillu
grup oraz zachowanie ograniczeń. git diff --check zaliczony.
Historyczne wyniki poniżej dotyczą wcześniejszych przebiegów.

## Zdjęcia profilu i prefiksy storage — weryfikacja 2026-11-10

User obsługuje własne zdjęcie w S3 albo wybór współdzielonego presetu, analogicznie
do Grouping. UserIconService korzysta z IObjectStorage bez zależności User → Grouping.
Migracja `20261109191600_UserImprovements` dodaje nullable text IconStorageKey
i CHECK rozłączności z Icon. W bazie pozostają klucze, odczyt zwraca podpisany URL.
Presety trzeba wgrać osobno; seed nie tworzy obiektów w S3. Dawne dowolne Icon
nie są automatycznie zamieniane na dostępne presety.

GET profile/icon-presets listuje obrazy z katalogu presetów, GET profile/icon
podpisuje aktualny klucz. PUT profilu obsługuje JSON i multipart z iconFile.
Własny plik: PNG/JPEG/WebP, limit 5 MiB, kontrola MIME i sygnatury; cały multipart
ma limit 6 MiB. Brak instrukcji zdjęcia zachowuje wybór (zmiana wcześniejszej
semantyki User); removeIcon usuwa wybór. Sprzeczne instrukcje dają 400.
Profil zwraca iconStorageKey. Klucza uploadu ani streamu nie ustawia klient JSON.
GET i PUT pozostają autoryzowane przez ICurrentUser i mają no-store.

Zapis blokuje rekord użytkownika. Potwierdzony rollback usuwa nowy upload;
potwierdzony commit pozwala sprzątnąć zastąpiony własny plik. Presety pozostają.
Błąd sprzątania nie cofa profilu, niepewny wynik commita zachowuje oba pliki.
Nie ma atomowej transakcji S3/PostgreSQL ani automatycznej rekonsyliacji.
Archiwizacja grup nadal pozostawia ich zdjęcia w S3.

Oba szablony env i Compose przekazują USER_ICON_PRESET_PREFIX,
GROUP_ICON_PRESET_PREFIX i RECEIPT_STORAGE_PREFIX. Receipt używa konfiguracji
dla nowych kluczy, zachowując odczyt historycznych StorageKey. Domyślne wartości,
kontrakty, błędy i ograniczenia opisuje
[development](development.md#zdjęcia-profilu-i-prefiksy-storage).

Weryfikacja na jednorazowym PostgreSQL 15: User.UnitTests 52/52, User.Tests 101/101,
User.IntegrationTests 77/77. Nowe przypadki sprawdzają formaty i limit pliku,
presety, signed URL, autoryzację, multipart i pola serwerowe, wybór/zachowanie/
usunięcie, błędy storage i bazy, równoległe podmiany, błąd commita przed oraz
po faktycznym zatwierdzeniu, CHECK i Up/Down/Up. Storage jest atrapą; PostgreSQL,
migracje, repozytoria, serwisy i JWT są rzeczywiste. Receipt.UnitTests 111/111,
w tym cztery warianty prefiksu i odczyt starego klucza.

Test HTTP wykrył niejednoznaczny PUT bez Content-Type; endpoint multipart ma
teraz niższy priorytet niż JSON. Test Google Down dostosowano do częściowego
cofnięcia późniejszych migracji przed zadziałaniem blokady Google.
Pełna regresja 13 projektów: 769 zaliczonych, 0 błędów, 1 pominięty ręczny test S3.
Release build API: 0 błędów/ostrzeżeń. Whitespace zmienionych testów i
git diff --check zaliczone. Postman User: 21 żądań, 42 skrypty ze sprawdzoną
składnią; nowe cztery skrypty zdjęć wykonane na danych syntetycznych.
Zaktualizowano kolekcję, środowisko i instrukcje.

Nie wykonano rzeczywistego uploadu do S3 ani prac na bazie aplikacji.
Powyższa weryfikacja z 2026-11-10 nie obejmowała frontendu i dekodowania/reskalowania;
ich późniejszą implementację opisuje wpis z 2026-11-12 na początku dokumentu.
Nie ma workera usuwającego osierocone pliki. Cofnięcie migracji usuwa
powiązania do własnych zdjęć, ale pozostawia pliki S3.

## Rejestracja i logowanie Google — weryfikacja 2026-11-10

User obsługuje Google Identity Services przez challenge z nonce i cookie
HttpOnly oraz endpointy google/login i google/register. Backend sprawdza podpis
RS256, issuer, audience, expiry i nonce ID tokenu; wydaje własny JWT i refresh
token. Integracja jest domyślnie wyłączona; wymaga publicznego Web ClientId.
Kontrakt, konfigurację i kody błędów opisuje
[development](development.md#rejestracja-i-logowanie-google).

Migracja `20261109191600_UserImprovements` dodaje nullable, globalnie
unikalny GoogleSubject typu text i dopuszcza brak PasswordHash z CHECK wymagającym
co najmniej jednej metody logowania. Down odmawia cofnięcia przy kontach bez
lokalnego hasła. Nie ma nowej tabeli ani automatycznego łączenia po mailu.
Nieaktywnego GoogleSubject nie można zarejestrować ponownie. Zmiana adresu
w Google nie nadpisuje zapisanego adresu aplikacji.

Nowe konto Google wymaga zwykłego loginu i danych profilu. Gmail/Workspace
z email_verified mogą otrzymać potwierdzony adres i sesję od razu; pozostałe
adresy przechodzą SMTP. Konto, grupa startowa i ewentualna sesja powstają
w jednej transakcji. Wysyłka SMTP następuje po commit; jej błąd pozostawia
konto i token, dostępny jest resend-confirmation. Wspólny IUserSessionService
utrzymuje jedną sesję dla logowania Google i hasłem.

Google user może ustawić pierwsze hasło przez istniejący
forgot-password/reset-password i nasz SMTP. Wymagany jest potwierdzony e-mail;
po ustawieniu hasła sesje są usuwane i działają obie metody logowania.
Zmiana maila jest blokowana także po ustawieniu hasła oraz przy potwierdzaniu
wcześniej wystawionego tokenu. Odłączania Google nie ma.
Profil zwraca hasPassword/isGoogleLinked, bez GoogleSubject i hasha hasła.

Weryfikacja na jednorazowym PostgreSQL 15: User.UnitTests 40/40,
User.Tests 90/90 i User.IntegrationTests 66/66, bez pominięć. Sprawdzono lokalną
walidację podpisu RSA przez atrapę discovery/JWKS, błędy tokenu i metadanych,
cookie/nonce, wygaśnięcie i współbieżne zużycie challenge, walidację DTO,
statusy/no-store/limity, oba logowania i unieważnienie sesji, pierwsze hasło,
SMTP/resend, brak łączenia kont, blokady adresu, współbieżną rejestrację/logowanie,
rollback konta i grupy przy błędzie sesji, UNIQUE/CHECK i Up/Down/Up migracji.
Starszy test triggera dostosowano do zapisu w schemacie sprzed GoogleSubject.

Regresja wszystkich 13 projektów zaliczona. Końcowe wyniki projektów po dodaniu
pięciu dodatkowych przypadków HTTP: 731 zaliczonych, 0 błędów, 1 pominięty ręczny
test S3. Release build API: 0 błędów i ostrzeżeń. Kontrola whitespace zmienionych
testów i git diff --check zaliczone. Kolekcja User ma 17 żądań: JSON i składnia
34 skryptów zweryfikowane; nowe skrypty Google wykonano na danych syntetycznych.

Nie wykonano rzeczywistego logowania w Google Identity Services ani wysyłki
SMTP. W powyższej weryfikacji z 2026-11-10 frontend nie był podłączony;
klienta Google i uzupełnienie profilu dodano 2026-11-12 (opis na początku dokumentu).
Przy odrębnym originie potrzebne będzie CORS z credentials
lub wspólne proxy. Cookie wymaga wspólnego site; HTTPS za reverse proxy wymaga
poprawnej konfiguracji zaufanych forwarded headers. Magazyn challenge działa
w pamięci jednej instancji; restart unieważnia próby, skalowanie wymaga sticky
sessions albo wspólnego magazynu. Bazy aplikacji nie zmieniano.

## Zwykła grupa startowa przy rejestracji — 2026-11-09

Migracja `20261109191600_UserImprovements` dodaje funkcję
`grouping.create_user_starter_group()` i trigger AFTER INSERT na tabeli User.
Każdy nowy użytkownik otrzymuje zwykłą grupę „Wydatki osobiste” w PLN i jedno
aktywne członkostwo administratora z zerowymi agregatami. Nie ma specjalnego
typu, blokad ani oznaczenia grupy osobistej; nazwa i członkostwa mogą się zmieniać.
Trigger działa w transakcji INSERT użytkownika, także dla seeda i ręcznego SQL.
Nie tworzy grup wstecznie; Down usuwa wyłącznie trigger i funkcję.
Nie zmieniono kodu rejestracji, kontraktu odpowiedzi ani zależności modułów.

Seed zachowuje pięć wygenerowanych grup startowych obok „Dom” i „Wyjazd”
(siedem grup łącznie); istniejące stałe ID przykładów pozostają zachowane.
Zaktualizowano opis rejestracji w kolekcji Postmana, instrukcję testów oraz
architecture.md, domain.md i development.md. Testy migracji e-maila wskazują
teraz właściwą migrację zamiast zakładać, że jest ostatnia; testy Grouping
uwzględniają dodatkowe grupy i członkostwa oraz częściowo zakończone cofanie EF.
Weryfikacja na tymczasowym PostgreSQL: pięć nowych przypadków triggera zaliczonych;
regresja User.IntegrationTests 51/51 i Notifications.IntegrationTests 13/13.
Po doprecyzowaniu zapytań członkostwa i założeń o historii EF ponowiono
odpowiednie zestawy: User 8/8 (trigger i unikalność e-maila), Banking 35/35
(model grup, seed i migracje). Pozostałe testy jednostkowe i HTTP regresji
zaliczone; jedyny pominięty przypadek to ręczny test S3.
Sprawdzono rollback konta, tokenu, grupy i członkostwa, brak wysyłki e-maila
przy błędzie zapisu, brak backfillu, zachowanie grup przy Down i ponowienie seeda.
Bazy aplikacji nie zmieniano; migracja wymaga osobnego zastosowania.

## Ponowne użycie e-maila po dezaktywacji — 2026-11-09

Migracja `20261109191600_UserImprovements` zastępuje globalną unikalność
e-maila indeksem częściowym `c_user_unique_email WHERE is_active = true`.
Obsługuje ograniczenie UNIQUE z SQL init oraz sam indeks. Login nadal jest
unikalny globalnie. Nowe konto otrzymuje nowe ID bez przejmowania historii.
GetActiveByEmailAsync i ExistsActiveByEmailAsync filtrują IsActive; korzystają
z nich rejestracja, logowanie, reset hasła, ponowna wysyłka potwierdzenia oraz
oba etapy zmiany e-maila. Logowanie e-mailem bez aktywnego konta daje 401;
login nieaktywnego konta z poprawnym hasłem nadal daje 403. Szczegóły i warunki
cofnięcia migracji: domain.md i development.md.

Weryfikacja: User.IntegrationTests 46/46 na tymczasowym PostgreSQL, bez pominięć.
Obejmuje ponowną rejestrację po dezaktywacji, zachowanie starego loginu,
nową tożsamość, lookup sesji i tokenów, zmianę adresu na adres nieaktywnego konta,
wiele nieaktywnych kont z jednym adresem, odrzucenie drugiego aktywnego konta
i reaktywacji, Up/Down/Up oraz rollback nieudanego Down przy powtórzonych adresach.
User.Tests 72/72, User.UnitTests 21/21. Regresja pozostałych projektów bez
konfiguracji usług zewnętrznych ujawniła test zakładający, że migracja Expense
musi być ostatnia; zastąpiono to sprawdzeniem kolejności migracji Grouping/Expense.
Ponowny przebieg Banking.IntegrationTests: 19 zaliczonych, 60 PostgreSQL pominiętych.
Pozostałe projekty regresji zaliczone; Notifications PostgreSQL i ręczny test S3
pominięto bez konfiguracji. Tymczasowy serwer PostgreSQL zatrzymano po testach.
Bazy aplikacji nie zmieniano; migracja jest przygotowana do osobnego zastosowania.

## Logowanie loginem lub e-mailem — 2026-11-09

`POST /api/users/login` przyjmuje w dotychczasowym polu `login` login albo
aktualny adres e-mail. Limit pola wynosi 254 znaki; normalizacja i wymagania
dotyczące hasła, aktywności konta oraz potwierdzenia e-maila pozostają zachowane.
Wartość z `@` jest wyszukiwana wyłącznie po e-mailu, a bez `@` po loginie.
Kontrakt opisuje development.md.
Nie zmieniono modelu ani migracji.

Rejestracja i edycja profilu odrzucają login zawierający `@` przez walidację
DTO (400 z błędem pola Login). Pole `login` przy uwierzytelnianiu nadal dopuszcza
adres e-mail. Istniejących loginów w bazie nie zmieniono; historyczny login
z `@` nie jest używany do uwierzytelniania — takie konto może użyć swojego e-maila.

Weryfikacja: User.UnitTests 21/21 i User.Tests 72/72 zaliczone, w tym cztery
przypadki HTTP odrzucenia `@` przy rejestracji i edycji profilu oraz cztery
przypadki odrzucenia e-maila bez `@` przez istniejące `[EmailAddress]`
(rejestracja, zmiana e-maila, ponowna wysyłka potwierdzenia i reset hasła).
User.IntegrationTests skompilowano; testy PostgreSQL pominięto bez
TEST_POSTGRES_CONNECTION_STRING. Dodano scenariusze logowania e-mailem,
normalizacji, odrzucenia niedostępnych kont, wyszukiwania wyłącznie po e-mailu
dla wartości z `@` oraz zmiany adresu.
Nie potwierdzono ich wykonania na bazie; nie zmieniano bazy aplikacji.

## Frontend developerski — 2026-11-03

`frontend/` zawiera ekrany konta, grup, rozliczeń, bankowości, powiadomień
i wydatków oraz klienta HTTP z JWT i odświeżaniem sesji. Nazwa `src/mocks/`
jest historyczna: katalog zawiera typy i pomocniki UI; dane pobiera `src/api/`.
Zsynchronizowano lockfile, podłączono Tailwind CSS 4 i poprawiono kolejność
providerów (UserProvider otacza Provider odczytujący sesję). Bez sesji aplikacja
wyświetla logowanie. Build obejmuje kontrolę typów TypeScript.

Przeglądarka używa względnego `/api`; proxy dev/preview kieruje żądania do
`API_PROXY_TARGET` (lokalnie localhost:5283, w Compose backend:8080).
Nie wymaga to CORS backendu. Jawne `VITE_API_BASE_URL` omija proxy i przy
osobnym origin wymaga CORS. Produkcyjne serwowanie dist wymaga osobnego
reverse proxy API lub jawnego adresu API. Compose używa Node 22, bind mount,
wolumenu zależności, npm ci przy starcie i portu 5173. Mobile i obraz produkcyjny
pozostają do implementacji. Polecenia opisują
[frontend](../frontend/README.md) i [kontenery](../containers/local/README.md).

Aktualna weryfikacja 2026-11-03: instalacja zależności i ponowne npm ci offline
z cache (również z włączonymi skryptami), build z kontrolą typów, Compose config --quiet i dwa
testy npm test zakończone powodzeniem. Testy sprawdzają render logowania z pełnym
drzewem providerów oraz przekazywanie POST, query, Authorization i odpowiedzi
401 przez serwer Vite do syntetycznego serwera HTTP. Nie potwierdzają działania
backendu z bazą ani integracji zewnętrznych. Docker Desktop jest wyłączony,
host przeglądarki niedostępny; bez nowego testu kontenerów i wizualnego UI.
Nie uruchamiano migracji ani seeda. Ówczesne historia/edycja/usuwanie Expense
i operacje Receipt/OCR zwracały 501. Obecnie są zaimplementowane i podłączone
do frontendu; wspólny dialog 501 pozostaje obsługą niedostępnych usług.

Wcześniejsza weryfikacja szablonu Vite (przed powyższymi zmianami): Compose config --quiet, build obrazu, start wyłącznie frontendu,
lint i build aplikacji wewnątrz kontenera oraz restart z istniejącym wolumenem
zakończone powodzeniem. HTTP zwraca 200 ze stroną startową. Po tymczasowej
zmianie App.tsx połączenie WebSocket Vite odebrało js-update bez restartu;
tekst szablonu przywrócono. Nie potwierdzono wizualnego działania strony ani
kliknięcia licznika: host dostępu do przeglądarki był niedostępny, a odczyt
stanu okna Chrome nie powiódł się. Nie uruchamiano migracji ani seeda.
Ówczesny `npm audit` zgłaszał 12 wpisów moderate wynikających z podatności minimatch
w drzewie narzędzi ESLint (zależności developerskie); bez high/critical.

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
  Samo współdzielenie połączenia nie zapewnia atomowości między modułami;
  obecnie transakcję zapewnia koordynator Grouping opisany wyżej.
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
osieroconych obiektów. ReceiptService nie sprawdza właściciela paragonu;
autoryzację zapewnia obecnie Expense przy udostępnianiu przez swoje endpointy.
Receipt zgodnie z decyzją z 2026-11-02 pozostaje modułem bez własnych endpointów;
Expense korzysta z jego serwisu. Jeden wydatek może mieć najwyżej jeden
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
Sam worker nie zapisuje Category/ExpenseItem. Obecnie Expense mapuje kategorie
do propozycji formularza, a frontend obsługuje korektę i zapis. Analiza i odczyt
HTTP są dostępne przez Expense; kolekcja Postman Storage nadal wywołuje
bezpośrednio SeaweedFS S3.

Opcje OCR i S3 mają `ValidateOnStart`. `STORAGE_PUBLIC_ENDPOINT` pozwala podpisywać
URL dla klienta innym adresem niż wewnętrzny endpoint S3. Receipt.IntegrationTests
zawiera ręczny test upload → podpisany GET → odmowa anonimowego GET → delete,
włączany przez `STORAGE_INTEGRATION_TESTS=1`. Obecny CI nie uruchamia SeaweedFS,
workera ani testów pytest. PostgreSQL CI obejmuje User, Banking, Notifications i Expense.
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
| User | Rejestracja i potwierdzenie e-maila, login lub e-mail, JWT i rotacja refresh, Google i pierwsze hasło, profil/ikony, zmiana e-maila, reset i dezaktywacja; klient webowy podłączony | Jedna sesja na konto; brak łączenia kont po e-mailu, zmiany adresu konta Google i zmiany hasła przez aktualne hasło. FRONTEND_URL wymagany przy starcie. Rzeczywiste Google/SMTP wymagają osobnej weryfikacji |
| Banking | Próby/połączenia, adapter Tink, callback, odczyt zgód oraz refresh on demand przez trwały worker PostgreSQL, import kont i BOOKED per rachunek; IBankingReadService oraz HTTP GET list/szczegółów rachunków i transakcji | Nowy import wymaga osobnego potwierdzenia Sandbox i zastosowania migracji AddBanking do właściwej bazy; brak odłączania banku i odnawiania zgód |
| Receipt / OCR | Analiza pliku bez zapisu, upload przy zapisie Expense, odczyt rekordu i podpisanego URL oraz usuwanie; StorageKey z unikalnym indeksem; dwa projekty testowe | Własne endpointy Receipt są celowo wykluczone; brak potwierdzenia pełnego przepływu z rzeczywistym OCR/storage w bieżącej weryfikacji; seed zawiera syntetyczny klucz bez pliku |
| Kategoryzacja | Klasyfikacja i propozycje nazw produktów w workerze Python; Expense mapuje nazwy kategorii na ID w propozycji formularza, którą użytkownik może poprawić | Worker nie zapisuje encji .NET; niejednoznaczna lub nieznana nazwa wymaga wyboru kategorii; historyczne eksperymenty nie potwierdzają jakości aktualnego pipeline |
| Grouping | Usługi i API grup, członkostw, zaproszeń, rozliczeń i sugestii, agregaty Expense i powiadomienie ExpenseAdded; szczegóły w README modułu | Odbudowa agregatów pozostaje do wykonania |
| Limits | Model i repozytorium CategoryGroupLimit oraz pola limitów grupy | Brak serwisów i API budżetów oraz przepływu alertów |
| Notifications | NotifyAsync z filtrowaniem aktywnych kont; inbox HTTP z paginacją i filtrem, odczyt pojedynczy/zbiorczy; klient webowy, licznik i dzwonek; Grouping generuje ExpenseAdded, GroupMemberJoined i SettlementReceived | Null bez odbiorców nie blokuje zapisu biznesowego. Limits nie generuje alertów; brak aplikacji mobilnej, push poza zakresem. Datowane testy poniżej dotyczą konkretnych rewizji |
| Reports | Cztery raporty HTTP przez ReportsService i IExpenseAggregationService; Expense agreguje dane w PostgreSQL | Starszy odczyt encji usunięto 2026-11-18; stan weryfikacji usług zewnętrznych opisują datowane wyniki |
| Web / mobile | React 19 + TypeScript + Vite 8 + Tailwind 4; widoki konta, grup, rozliczeń, wydatków/OCR, Banking i inboxa korzystają z API; proxy i kontener developerski | Brak projektu mobile/Capacitor, raportów/limitów UI i produkcyjnego obrazu frontendu. Testy Node/SSR i build nie potwierdzają interakcji w przeglądarce ani usług zewnętrznych |
| Narzędzia | Hooki, testy hooków, workflow backendu z PostgreSQL i wykrywaniem UnitTests/Tests/IntegrationTests; siedemnaście projektów w solucji | CI ustawia wspólną zmienną TEST_POSTGRES_CONNECTION_STRING dla testów bazy User, Banking, Notifications, Expense i Reports. Runner wybiera tylko śledzone projekty; „not configured” nie oznacza wykonania testów |
| Infrastruktura | Wspólny Compose pięciu usług: frontend, PostgreSQL, backend, SeaweedFS i OCR z jednym containers/local/.env; Terraform Proxmox z workflow | containers/deploy zawiera tylko .gitkeep; bez nowego uruchomienia kontenerów, migracji ani wdrożenia podczas przeglądu; AWS pozostaje planem |

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
przepływ Sandbox pozostają niepotwierdzone. Brak `ContinuousAccessVerified`
lub false blokuje rzeczywiste operacje. Obecne appsettings.json i szablon
Compose ustawiają true, fallback Compose ma false. Sprawdzaj efektywną
konfigurację; true nie dowodzi uprawnień Tink. Walidacja Tink odbywa się przy użyciu,
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

Przegląd 2026-11-15 wykrył także niezgodności starszego kontekstu z kodem:
Expense → Group/Receipt/BankTransaction i ExpenseShare → User nie mają
jawnych mapowań EF, choć FK istnieją w SQL init. AppDbContext mapuje płatnika
Expense i Notification → Expense. Obecna unikalność importowanego rachunku
opiera się na parze TinkUserId/TinkAccountId; numer, IBAN i instytucja są opcjonalne.

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
| Expense wymaga grupy także dla wydatków osobistych | Nowe konto otrzymuje zwykłą grupę startową „Wydatki osobiste” w PLN; może ją normalnie zmieniać i rozszerzać. Istniejące konta bez backfillu |
| Historyczny SQL init i diagramy mogą nadal zawierać ExpenseItem.fk_user_id | Migracja CompleteExpenseModel usuwa pole i FK; MVP obejmuje podział całego wydatku. Schemat docelowy opisuje domena |
| Dokumentacja stosu wymienia Swagger, mobile i Python OCR | Backend .NET ma OpenAPI w Development; worker FastAPI ma /docs i OCR. Web React jest już w repo i korzysta z API; brak projektu aplikacji mobilnej |
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
Ówczesny zakres nie obejmował ExpenseDbContext; obecnie dołącza do koordynatora.
Odbudowa agregatów nadal nie jest wdrożona; niepusty
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

Opis historyczny. Tymczasową migrację poniżej zastąpiła CompleteExpenseModel;
bieżący łańcuch opisuje [development](development.md#migracje-po-rebase-expensegrouping--2026-11-03).

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
