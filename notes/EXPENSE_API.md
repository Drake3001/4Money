# Expense API — kontrakt dla frontendu

**Kontrakt Expense z integracją Receipt, wersja 0.4.0.**
Działają kategorie, lista dostępnych transakcji, historia, szczegóły, tworzenie,
preview tworzenia i edycji oraz PUT i DELETE wydatku. Zmiany wydatku i sald
Grouping zapisują się w jednej transakcji.

OCR analizuje plik bez zapisu. Paragon jest zapisywany razem z wydatkiem
i odczytywany przez jego ID. Jeden oryginalny wynik OCR jest zachowany
w `Receipt.ExtractionResult`.
Utworzenie wydatku wysyła ExpenseAdded do pozostałych aktualnych członków grupy
z aktywnymi kontami. Edycja i usunięcie nie wysyłają powiadomień.
Aktualny stan i faktyczne kontrole opisuje
[status implementacji](../agent-context/implementation-status.md).

Pełne, maszynowo czytelne DTO, parametry i przykłady są w
[OpenAPI 3.0.3](EXPENSE_API.openapi.yaml). Plik jest samodzielny: można go
zaimportować do narzędzia obsługującego OpenAPI, np. edytora Swagger lub Postmana.
Nie trzeba uruchamiać backendu. Adres `http://localhost:5283` oznacza docelowy
lokalny profil HTTP. Po uruchomieniu hosta w Development wygenerowany dokument
z deklaracjami jest dostępny pod `/openapi/v1.json`. Host nie ma wbudowanej strony
Swagger UI; JSON można otworzyć w kliencie OpenAPI. Samodzielny YAML poniżej
zawiera także przykłady i szczegółowe reguły docelowej implementacji.
Wszystkie ID, osoby, adresy storage i dane zakupowe w przykładach są syntetyczne.

## 1. Operacje

Kolumna „Sukces” obejmuje operacje wdrożone i docelowe. Status każdej operacji
jest oznaczony w OpenAPI. Preview tworzenia
wykonuje kontrole i obliczenia, ale nie zapisuje ani nie rezerwuje źródła.

| Metoda i ścieżka | Request / parametry | Sukces | Znaczenie |
| --- | --- | --- | --- |
| `GET /api/categories` | Brak | `200 CategoryResponse[]` | Cały słownik, bez paginacji; nazwa, opis, Lucide i oznaczenie kategorii systemowej |
| `GET /api/expenses` | Filtry historii, paginacja, sortowanie | `200 ExpensePage` | Lista dostępnych wydatków z danymi do wyświetlenia |
| `GET /api/expenses/{expenseId}` | GUID wydatku | `200 ExpenseResponse` | Wszystkie produkty i udziały, źródła, wersja, uprawnienia |
| `POST /api/expenses/preview` | `PreviewExpenseRequest` | `200 ExpensePreviewResponse` | Walidacja i obliczenia bez zapisu |
| `POST /api/expenses` | `CreateExpenseRequest` | `201 ExpenseResponse`, `Location` | Utworzenie kompletnego wydatku i przypięcie źródeł |
| `PUT /api/expenses/{expenseId}` | `UpdateExpenseRequest` | `200 ExpenseResponse` | Pełny zapis formularza; nowa wersja |
| `DELETE /api/expenses/{expenseId}` | GUID i wymagany query `version` | `204`, bez body | Trwałe usunięcie wydatku i paragonu |
| `GET /api/expenses/available-bank-transactions` | Filtry bankowe, paginacja, sortowanie | `200 AvailableBankTransactionPage` | Własne dostępne obciążenia PLN |
| `POST /api/expenses/receipts/analyze` | `multipart/form-data`, jeden `file` | `200 ReceiptOcrResponse` | OCR bez zapisu oraz podpisany wynik do zapisu wydatku |
| `GET /api/expenses/{expenseId}/receipt` | GUID wydatku | `200 ReceiptResponse` | Podpisany URL i zapisany wynik OCR |

`Location` jest generowane przez routing i wskazuje odpowiedni GET szczegółów,
np. `http://localhost:5283/api/expenses/70000000-0000-0000-0000-000000000001`.
Nie dodajemy osobnych endpointów produktów, udziałów, przypinania banku,
stosowania OCR, biblioteki paragonów ani `summary`.

## 2. Format i DTO

Wszystkie operacje wymagają JWT aplikacji User (`Authorization: Bearer ...`).
Tożsamość wywołującego pochodzi z `ICurrentUser`, nie z `userId` w query/body.
`payerId`, `participantId` i `shares[].userId` mają znaczenie domenowe, nie
zastępują tożsamości ani autoryzacji. Odpowiedzi prywatne mają `Cache-Control: no-store`.

- JSON `camelCase`; requesty JSON z `Content-Type: application/json`.
- GUID musi być poprawny i niezerowy. Nullable ID źródła przyjmuje GUID albo JSON `null`.
- Kwoty i procenty są **tekstami**, np. `"100.00"`; ilości np. `"0.735"`.
  Bez przecinka, spacji, plusa, notacji wykładniczej i separatorów tysięcy.
  Request może mieć mniej miejsc po kropce. Odpowiedź ma 2 miejsca dla kwot
  i procentów, 3 dla ilości. Kwoty mieszczą się w `numeric(18,2)`, ilości w `numeric(18,3)`.
- `transactionTime` jest wymaganą datą `YYYY-MM-DD`, bez godziny i strefy; przyszłe daty są dozwolone.
  POST, PUT i preview przyjmują wyłącznie datę; szczegóły i historia zwracają ten sam format.
  Backend zapisuje datę jako północ UTC w istniejącej kolumnie `transaction_time`
  (`timestamp with time zone`), bez migracji. Starsze rekordy zwracają datę części UTC,
  bez zmiany zapisanego czasu. Frontend nie przelicza daty przez lokalną strefę.
- `name` pozycji i wydatku jest wymagane, niepuste po trim. Kontrakt nie dopisuje
  arbitralnych limitów długości, których nie ma w obecnych DTO.
- `merchant` i `unit`: pominięcie, `null` lub biały tekst oznaczają brak wartości;
  pozostały tekst jest przycinany. W PUT pominięcie sprzedawcy go czyści.
- Nieznane pola requestów nie są częścią kontraktu (schematy są zamknięte).
  Nie kopiuj całej odpowiedzi GET wprost do requestu zapisu.

### Formularz tworzenia i edycji

| Pole | POST | PUT |
| --- | --- | --- |
| `groupId` | Wymagane | Niedozwolone; grupa jest niezmienna |
| `payerId`, `name`, `transactionTime` | Wymagane | Wymagane |
| `items`, `shares` | Wymagane tablice | Wymagane pełne tablice zastępujące poprzednie |
| `amount` | Opcjonalne, nullable | Opcjonalne, nullable; brak nie oznacza zachowania starej kwoty |
| `merchant` | Opcjonalne, nullable | Opcjonalne, nullable |
| `bankTransactionId` | Opcjonalne; brak lub null oznacza brak banku | Wymagane; null odłącza bank |
| `removeReceipt` | Niedozwolone | Opcjonalne; `true` usuwa paragon. Nie łączy się z nowym plikiem |
| `version` | Niedozwolone | Wymagany aktualny token ze szczegółów |

Pozycja requestu zawiera `categoryId`, `name`, `unitPrice`, `quantity`, opcjonalne
`discount` (domyślnie `"0.00"`), `unit` oraz `id`.
Przy tworzeniu `id` należy pominąć lub podać null. Przy edycji ID dotychczasowej
zwykłej pozycji zachowuje jej tożsamość; nowa ma ID pominięte/null.
Nie wolno wysłać duplikatów ID, ID cudzych pozycji ani pozycji systemowych.
Pominięcie starej zwykłej pozycji w tablicy PUT usuwa ją.

Udział requestu to `userId` i `percentage`. Lista jest niepusta, użytkownicy
unikalni, każdy procent > 0 i <= 100, suma dokładnie 100. Osoby z zerowym
procentem frontend pomija. API nie przyjmuje trybu podziału ani wejściowych kwot/proporcji.

### Odpowiedzi dla widoków

`ExpenseResponse` zachowuje podstawowe pola istniejącego DTO i dodaje
`groupName`, `payerDisplayName`, `currencyCode: "PLN"`, `version`, `canEdit`, `canDelete`.
Pozycje zawierają ID, kategorię (`categoryId`, `categoryName`, `categoryIcon`),
dane produktu, końcowe `total` i `isSystemGenerated`. Udziały zawierają
`userId`, `userDisplayName`, `percentage` oraz końcowe `amount`.
Nazwy grup i osób to bieżące etykiety, nie historyczne snapshoty profilu.
Nie zwracamy e-maili, danych rachunków ani historii bankowej innych członków.

`ExpenseListItemResponse` zawiera podstawowe dane zakupowe i źródła, nazwy grupy
i płatnika, `myShareAmount`, `mySharePercentage`, unikalną tablicę `categories`
oraz `hasReceipt` i `hasBankTransaction`. Własny udział to `"0.00"`, jeśli
wywołujący nie uczestniczy w koszcie. Bez produktów i pełnej listy udziałów;
te są dostępne jednym GET szczegółów, bez paginacji. Pozycje szczegółów
są stabilnie porządkowane po ID; API nie utrwala kolejności wpisywania produktów.

Kategorie mają `id`, `name`, `description`, `icon`, `isSystemGenerated`.
Flaga kategorii oznacza systemowe „Nieznane”, a flaga pozycji automatyczne
dopełnienie. Kategorie systemowe wyświetlamy, ale wyłączamy z ręcznego wyboru.
Kolor ustala frontend. Słownik jest sortowany po nazwie i ID.

## 3. Kwota, preview i współbieżność

Końcowa wartość zwykłej pozycji to
`Round(unitPrice × quantity, 2, AwayFromZero) + discount`.
Ilość musi być dodatnia, cena może być ujemna/zerowa/dodatnia. Rabat jest
kwotowy dla całej pozycji i <= 0. Nie może uczynić nieujemnej pozycji ujemną;
przy osobnej ujemnej pozycji rabatowej `discount` musi wynosić 0.

- Podane `amount` jest celem. Mniejsza suma zwykłych pozycji powoduje dodatnie
  dopełnienie „Nieznane”; większa jest błędem 400. Usunięcie produktu może więc
  zwiększyć dopełnienie, bez zmiany całkowitej kwoty.
- Pominięte/null `amount` oznacza wyliczenie kwoty wyłącznie ze zwykłych produktów,
  także w PUT. Wynik musi być dodatni. Nie tworzymy zerowej pozycji „Nieznane”.
- Pusta lista produktów i podane dodatnie `amount` daje jedno systemowe „Nieznane”.
  Pusta lista bez kwoty jest błędem. Systemowych pozycji z GET nie odsyłamy w formularzu.
- Podział groszy: największe reszty; remisy według rosnącego tekstu GUID (ordinal).
  Dodatni procent może otrzymać kwotę `"0.00"`. Suma kwot udziałów zawsze równa
  kwocie wydatku. Konwersja wejściowych kwot do procentów z dokładnością 0,01%
  może zmienić pierwotnie wpisaną kwotę udziału; frontend pokazuje wynik backendu.

`PreviewExpenseRequest` ma formularz tworzenia oraz opcjonalne `expenseId` i
`version`. Bez expenseId podgląda tworzenie. Przy expenseId wymaga aktualnej
version i groupId zgodnego z istniejącym wydatkiem. Może wtedy
zachować dotychczasowy bank i dawnych uczestników, tak jak PUT.
bankTransactionId wskazuje wybrany bank; pominięcie lub null podgląda odłączenie.
Paragon nie jest polem requestu preview; odpowiedź zawiera ID obecnego paragonu.
Odpowiedź zawiera końcową kwotę, pozycje (bez przydzielania trwałych ID), udziały
i wybrane ID źródeł. Nie tworzy wydatku, pliku, OCR, powiadomień ani rezerwacji.
Zapis ponawia wszystkie kontrole, ponieważ stan może zmienić się po podglądzie.

PUT wymaga `version` w body, DELETE query `version`. Token należy kodować jak
zwykły parametr URL i traktować jako nieprzezroczysty. Brak/pusty token: 400;
nieaktualny: 409. Po sukcesie PUT używamy nowej wersji. Sam podgląd nie zmienia
wersji. Wersja powinna chronić cały formularz i powiązania, nie tylko kolumny Expense.

Aktualni członkowie grupy mogą tworzyć, edytować i usuwać. Byli członkowie,
którzy są płatnikiem lub uczestnikiem konkretnego wydatku, mają tylko odczyt
wydatku i jego paragonu. Aktualny członek może zmienić/usunąć istniejący udział
byłego członka, ale nie dopisać go jako nowego uczestnika; istniejącego byłego
płatnika może zachować. Nowy płatnik i nowi uczestnicy muszą być aktualnymi członkami.
Flagi UI nie zastępują kontroli backendu. Nieuprawniony odczyt daje 404;
próba zapisu przez osobę uprawnioną tylko do odczytu — 403.

## 4. Historia i paginacja

Używamy istniejących `ZPI4M.Shared.Pagination.PagedQuery` i `PagedResult`,
jak Banking i Notifications. Parametry `pageNumber=1`, `pageSize=20`;
pageNumber >= 1, pageSize 1–100, offset musi mieścić się w int32.

```json
{
  "items": [],
  "totalCount": 0,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 0,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

`totalPages = ceil(totalCount / pageSize)`, `hasPreviousPage = pageNumber > 1`,
`hasNextPage = pageNumber < totalPages`, dokładnie jak we wspólnej klasie.
Pusta strona (także poza zakresem wyników) daje 200 i zachowuje metadane.
totalCount obejmuje wyniki po autoryzacji i wszystkich filtrach, przed paginacją.

| Filtr historii | Znaczenie |
| --- | --- |
| `groupId` | Grupa, także osobista; przy braku participantId wszystkie dostępne wydatki grupy |
| `participantId` | Uczestnik kosztu, nie autor; nie rozszerza uprawnień |
| `payerId` | Płatnik; sam nie wyłącza domyślnego filtra moich udziałów |
| `transactionTimeFrom`, `transactionTimeTo` | Daty YYYY-MM-DD: początek włącznie, koniec wyłącznie; from < to |
| `minAmount`, `maxAmount` | Tekstowe dodatnie kwoty całego zakupu, granice włącznie; min <= max |
| `categoryId` | Co najmniej jedna pozycja kategorii, również „Nieznane”; wynik nadal obejmuje cały wydatek |
| `search` | Fragment nazwy wydatku lub sprzedawcy, bez rozróżniania wielkości liter; nie przeszukuje produktów |
| `hasReceipt`, `hasBankTransaction` | Opcjonalne boolean; brak nie filtruje, false oznacza brak źródła |
| `sortBy`, `descending` | transactionTime/amount/name, domyślnie transactionTime i true |

Bez groupId i participantId obowiązuje participantId zalogowanego użytkownika,
niezależnie od pozostałych filtrów. Np. payerId bez groupId nadal zawęża moje
udziały, a nie pobiera wszystkich widocznych zakupów płatnika. Wskazanie
participantId bez grupy przeszukuje tylko wydatki dostępne wywołującemu.
Były członek grupy nadal widzi jedynie wydatki, do których ma odczyt.

Filtry łączymy przez AND. Puste/białe filtry tekstowe ignorujemy po trim;
niepoprawnych GUID, liczb i dat nie ignorujemy (400). SortBy rozpoznajemy bez
rozróżniania wielkości liter, jak Banking; nieznana wartość daje 400.
Remisy sortowania rozstrzygamy po ID rosnąco. To paginacja offsetowa, bez
gwarancji snapshotu przy równoczesnym dodawaniu/usuwaniu wydatków.

## 5. Transakcja bankowa jako źródło

Selektor wywołuje `GET /api/expenses/available-bank-transactions`.
Zwracane pola odpowiadają Banking: id, bankAccountId, amount (ujemne),
currencyCode=PLN, date, description, status=Booked. Nie ma zdalnej synchronizacji.
Rachunki frontend pobiera z istniejącego `GET /api/banking/accounts`.

Filtry: `bankAccountId`, `dateFrom`, `dateTo`, `minAmount`, `maxAmount`,
`description`. Daty bankowe są typu date i obie granice są włącznie.
Kwoty filtrów zachowują znak bankowy, np. od `"-200.00"` do `"-50.00"`.
Sortowanie jak w Banking: `sortBy=date|amount|status|currencyCode`, domyślnie
date, `descending=true`; wspólna paginacja. Nie wystawiamy zmiennych filtrów
status/currencyCode, bo kwalifikacja źródła zawsze wymusza Booked i PLN.

Expense wyklucza globalnie przypisane transakcje **przed** paginacją i liczeniem
totalCount. Nie wolno filtrować tylko gotowej pojedynczej strony Banking.
Banking stosuje OnlyDebits i sprawdza rejestr przypisań przed Count/Skip/Take.
Lista pomija transakcje przypisane do wydatków i do rozliczeń grupowych.
Zapis, podmiana i zwolnienie przypisania odbywają się w tej samej transakcji
co zmiana wydatku.

Przypięcie/wymiana: w formularzu ustaw bankTransactionId, payerId na właściciela
i amount na bezwzględną kwotę obciążenia. Przy banku amount jest wymagane nie-null.
Backend odrzuca niespójność tych pól jako 400, a zajęcie źródła jako 409.
Nazwa, sprzedawca, data i produkty pozostają świadomym wyborem formularza;
bank nie nadpisuje ich potajemnie. Produkty nie mogą przekroczyć bankowej kwoty.

Aktualna przypięta transakcja nie występuje na liście dostępnych; jej ID zwraca
GET Expense. Uprawniony członek może zachować to ID przy edycji także wtedy,
gdy transakcja należy do innego członka. Nie uzyskuje przez to dostępu do
prywatnych danych Banking. Nowo wybierana transakcja musi należeć do wywołującego.
Odłączenie (`bankTransactionId: null`) zwalnia transakcję i odblokowuje kwotę
i płatnika; pozostałe wartości zapisują się zgodnie z pełnym formularzem PUT.

## 6. Paragon i OCR

Front utrzymuje wybrany plik i podgląd w formularzu. Analiza przyjmuje jeden
plik do 20 MiB i zwraca `proposal`, `warnings`, `needsReview` i `ocrProof`.
Nie tworzy rekordu ani obiektu w storage. Obsługiwane typy to JPEG, PNG, WebP,
HEIC/HEIF, TIFF, BMP i PDF.

Analiza ma limit pięciu żądań na minutę na użytkownika w instancji API.
Po wyczerpaniu limitu kolejne żądania zwracają HTTP 429 bez kolejkowania.

`proposal.transactionTime` zawiera datę zakupu `YYYY-MM-DD`, tak jak formularz
Expense. Jeśli OCR nie odczytał poprawnej daty, pole ma wartość `null`.
Każda pozycja `proposal.items` zawiera flagę `needsReview` i opcjonalny powód
`reviewReason` (`null`, gdy OCR nie podał powodu). Te same pola są dostępne
w `analysis.proposal.items` przy odczycie zapisanego paragonu.
W obu odpowiedziach `warnings` to lista obiektów z `code`, `message` i `field`.
`field` może wskazywać pole pozycji, np. `items[0].total`, albo mieć wartość
`null`. Starsze ostrzeżenia tekstowe mają `code` i `field` równe `null`.

Przy zapisie z plikiem wyślij `multipart/form-data`. Pole `expense` zawiera
formularz serializowany jako tekst JSON, `file` to wybrany plik, a `ocrProof`
to opcjonalny tekst z analizy. Przykład po stronie przeglądarki:

```javascript
const body = new FormData()
body.append("expense", JSON.stringify(expense))
body.append("file", file)
if (ocrProof) body.append("ocrProof", ocrProof)
```

Przeglądarka ustawia `Content-Type` wraz z separatorem multipart. Bez nowego
pliku użyj JSON. Zwykły PUT zachowuje paragon, plik go dodaje lub wymienia,
a `removeReceipt: true` usuwa. Formularz preview nie wysyła zdjęcia.

Podpisany wynik jest ważny przez 24 godziny dla tego samego użytkownika i bajtów
pliku. Po zmianie pliku odrzuć poprzedni proof. Po `400` zachowaj formularz i
pozwól ponowić analizę albo zapisać samo zdjęcie. Przed zapisem reset korzysta
z propozycji w formularzu. Po zapisie pobiera `analysis` z
`GET /api/expenses/{expenseId}/receipt`. Odpowiedź zawiera też `fileUrl` i
`fileUrlExpiresAt`. Gdy zdjęcie zapisano bez OCR, `analysis` jest null.
Reset uzupełnia dane zakupowe bez zmiany grupy, płatnika i uczestników.
Zapis formularza i reset nie wywołują OCR.

Ustaw `RECEIPTOCRPROOF__SIGNINGKEY` na osobny losowy klucz zakodowany w Base64
o długości co najmniej 32 bajtów po dekodowaniu. Wszystkie instancje API muszą
używać tego samego klucza. Klucz jest wymagany przy starcie API. Brak klucza, niepoprawny Base64 lub zbyt
krótki klucz zatrzymują start z błędem konfiguracji.

POST i PUT korzystają z osobnych akcji JSON i `[FromForm]` dla multipart,
zgodnie ze wzorcem Grouping. Upload następuje po wstępnej walidacji i przed
rozpoczęciem transakcji zapisu. Członkostwo i wersja są ponownie sprawdzane
przy zapisie. Błąd rozpoczęcia transakcji lub potwierdzony rollback sprząta
nowy plik. Niepewny wynik commitu zachowuje go do uzgodnienia.

Serwer zachowuje oryginalny JSON i oddzielnie zatwierdzone dane Expense.
Rekord usuniętego lub wymienionego paragonu znika z bazy w transakcji Expense.
Plik jest sprzątany po commicie. Stare nieprzypisane uploady wymagają osobnego
sprzątania.

## 7. Błędy i zachowanie klienta

Odpowiedzi błędów mają `application/problem+json`, standardowe ProblemDetails,
bez własnej koperty i bez obowiązkowego pola code. Walidacja zwraca
ValidationProblemDetails z errors, np. `items[0].quantity`; pusty klucz oznacza
cały formularz. Nie steruj UI na podstawie tekstu title/detail; struktura errors
i status HTTP są kontraktem, treść komunikatu może się zmieniać.

| Status | Zastosowanie |
| --- | --- |
| 400 | Niepoprawny request/filtr, brak wymaganej wersji, błędne udziały, nadmiar produktów, niezgodność kwoty/płatnika z bankiem |
| 401 | Brak/nieważny JWT lub niedostępne konto; standardowe wyzwanie Bearer |
| 403 | Odczyt dozwolony, ale dana operacja zapisu niedozwolona |
| 404 | Zasób nie istnieje lub nie jest dostępny do odczytu; nie ujawniamy cudzych danych |
| 409 | Nieaktualna wersja, zajęte źródło, równoległa zmiana wydatku |
| 413 | Przekroczony limit rozmiaru uploadu/żądania |
| 415 | Nieobsługiwany typ requestu lub pliku |
| 422 | Dokument niepoprawny, przekracza limity stron/pikseli albo OCR nie dostarcza użytecznych danych |
| 502 | Niepoprawna odpowiedź usługi OCR |
| 503 | Niedostępność bazy, storage lub OCR |
| 504 | Timeout OCR |

OpenAPI przypisuje statusy do konkretnych operacji i zawiera przykłady.
Przykład błędu walidacji:

```json
{
  "type": "about:blank",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "items[0].quantity": ["Quantity must be positive decimal text."] }
}
```

Po 409 zachowaj lokalny formularz, pobierz aktualny stan i pozwól użytkownikowi
świadomie rozwiązać konflikt. Nie nadpisuj automatycznie i nie ponawiaj zapisu
w pętli. Po utracie odpowiedzi POST/PUT lub 503 wynik zapisu może być niepewny:
ochrona idempotency pozostaje poza zakresem; przed powtórzeniem sprawdź stan.
Po DELETE odśwież historię. W PUT nie wystarczy wersja z dawnego wiersza listy:
otwarcie edycji pobiera szczegóły z aktualnym tokenem.

## 8. Zależności i granice prac

| Obszar | Co musi dostarczyć późniejsza implementacja |
| --- | --- |
| Expense | Działają historia, preview, tworzenie, szczegóły, edycja i usuwanie. Zapis obsługuje plik i opcjonalny wynik OCR |
| Receipt | Analiza bez zapisu, docelowy upload i podpisane URL-e |
| Integracja Expense/Receipt | Przypięcie w transakcji Expense/Grouping i konflikt 409. Odpięcie i usunięcie wydatku uruchamiają sprzątanie po commicie |
| Sprzątanie — inna osoba | Historyczne nieprzypisane rekordy i ponowienia fizycznego usunięcia plików; bez nowego endpointu dla frontendu |
| Grouping | Rzeczywiste członkostwa, nazwy, waluta i archiwum. Tworzenie, edycja i usuwanie pod blokadą grupy aktualizują TotalPaid/TotalShare; liczniki rozliczeń zachowują istniejące zachowanie |
| User | Wdrożony zbiorczy lookup nazw do autoryzowanych widoków, także nieaktywnych historycznych kont; bez prywatnych profili |
| Banking | OnlyDebits i filtry rejestru przypisań przed Count/Skip/Take, bez zależności Banking → Expense |
| Reports | Podsumowania i raporty. Expense udostępni potrzebne dane serwisem; nie dodajemy /api/expenses/summary |
| Notifications | Integracja po zapisie biznesowym, bez nowych endpointów Expense. Polityka odbiorców nadal wymaga ustalenia; bez infrastruktury push |

## 9. Scenariusze odbioru kontraktu

| Scenariusz | Oczekiwany rezultat kontraktu |
| --- | --- |
| Ręczny zakup, amount pominięte, produkt 70 zł | preview i zapis wyliczają 70 zł; bez systemowej pozycji za 0 zł |
| amount=100 zł, produkty 70 zł | Pozycje 70 + systemowe 30; po usunięciu produktów dopełnienie 100 |
| Produkty przekraczają podane amount | 400 z błędem formularza, bez zapisu części danych |
| 0,01 zł, po 50% dla dwóch osób | Końcowe udziały 0,01 i 0,00 według GUID, suma 0,01 |
| Edycja istniejącego produktu | Pełne tablice, ID zwykłej pozycji i aktualna version; nowy token po sukcesie |
| Nieaktualny PUT lub DELETE | 409, bez nadpisania nowszego stanu |
| Przypięcie własnego bankowego -100 zł | Wymagane amount=100 i właściciel jako płatnik; po sukcesie źródło znika z dostępnych |
| Źródło zajęte między listą/preview a zapisem | 409; sama lista ani preview nie rezerwują źródła |
| Edycja przez innego członka z zachowaniem banku | Dotychczasowe ID dozwolone; brak dostępu do prywatnego Banking właściciela |
| Odłączenie banku | PUT z null, źródło ponownie dostępne; kwota/płatnik odblokowane |
| Upload tylko obrazu | 201, odczyt paragonu zwraca analysis null |
| OCR i ręczna korekta | Częściowy proposal + ostrzeżenia; zapis poprawionego Expense nie zmienia oryginalnej analizy |
| Wymiana paragonu A → B | A usuwany dopiero po sukcesie zapisu B; błąd pozostawia A |
| Anulowanie formularza | Zamknięcie formularza odrzuca lokalny plik i proof |
| Były uczestnik | GET Expense/paragon dozwolony, flagi zapisu false, próba mutacji 403 |
| Historia lub bank bez wyników | 200 z items=[] i metadanymi PagedResult |

Weryfikacja dokumentacji obejmuje walidację OpenAPI, lokalnych $ref i przykładów
oraz sprawdzenie powyższych scenariuszy na poziomie kontraktu. Nie jest testem
działającego HTTP, bazy, OCR ani storage. Wyniki faktycznie wykonanych kontroli
są odnotowane w statusie implementacji, oddzielnie od wcześniejszych testów backendu.


## 10. Uruchomienie Expense bez resetowania danych

1. Zastosuj nową migrację `20261102210228_AddExpenseVersion` na właściwej lokalnej
   bazie. Z katalogu `backend/ZPI4M.Backend/src/ZPI4M.Api`:

   ```powershell
   dotnet ef database update --project ../ZPI4M.Database --startup-project . --context AppDbContext --configuration Release
   ```

   Obecny łańcuch wymaga SQL init i migracji CompleteExpenseModel po AddGroupingModel.
   Squashed migracja Expense wymaga pustych wydatków/pozycji/udziałów; nie jest
   przejściem przyrostowym dla wcześniejszej bazy. Szczegóły: instrukcja pracy.

2. Uruchom API, np. profilem `http`. Rzeczywiste usługi Grouping są używane także
   poza Development. Nie ma flagi UseGroupingMock. Expense obsługuje PLN.

3. Zaloguj się istniejącym kontem i wybierz istniejącą grupę, do której należy
   ono, płatnik i uczestnicy. Jeśli lokalnie masz dotychczasowy syntetyczny seed,
   grupa Dom to `20000000-0000-0000-0000-000000000001`, Anna to
   `10000000-0000-0000-0000-000000000001`, Bartek kończy się `0002`,
   kategoria spożywcza to `30000000-0000-0000-0000-000000000001`.
   Nie uruchamiaj seeda tylko w celu wypróbowania endpointów: seed usuwa dane.
   Ten etap nie tworzy grup ani użytkowników i nie dostarcza list grup/członków.

4. Pobierz kategorie, wyślij `PreviewCreate` z OpenAPI, potem `CreateManual`,
   a na końcu GET ze ścieżki Location. Preview nie zmienia liczby wydatków.
   Przy banku najpierw pobierz dostępne źródła i użyj własnego zwróconego ID,
   dodatniej bezwzględnej kwoty i siebie jako płatnika. Stałe przykładowe ID
   z YAML nie gwarantuje istnienia/dostępności transakcji w Twojej bazie.
   Pusta lista jest poprawna: np. zaksięgowane obciążenia starego seeda są już przypisane.

Lookup metadanych Grouping jest wewnętrzny i wykonywany po autoryzacji Expense,
więc były płatnik/uczestnik zachowuje etykiety historycznego wydatku. Tworzenie
ponownie sprawdza członkostwo, archiwum i walutę pod blokadą grupy. Wyliczone
kwoty udziałów sumują się dokładnie do wydatku (największe reszty, remis po UUID).
Błąd zapisu dowolnego modułu wycofuje wydatek i agregaty. Konflikt źródła bankowego
lub niespójność/przepełnienie agregatów daje 409. Preview/GET nie zmieniają sald.
PUT i DELETE aktualizują agregaty w tej samej transakcji co Expense.
ExpenseAdded jest zapisywane po utworzeniu wydatku, przed wspólnym commitem,
przez GroupExpenseNotificationService. Autor operacji nie jest odbiorcą,
również gdy wskazał innego płatnika. Brak odbiorców nie blokuje zapisu;
błąd zapisu powiadomienia wycofuje wydatek i salda. Po usunięciu wydatku
starsze powiadomienie zachowuje treść, a jego ExpenseId ma wartość null.
