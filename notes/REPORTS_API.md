# Reports — kontrakt serwisu i HTTP

Stan: 2026-11-18. ReportsService korzysta z IExpenseAggregationService, nie z list
wydatków ani repozytoriów Expense. Nazwy kategorii pobiera przez publiczny
IExpenseService.GetCategoriesAsync. Expense wykonuje agregację i zachowuje
istniejący dostęp do danych przez mechanizmy Grouping i historii Expense.

## Wspólne parametry

Wszystkie cztery requesty dziedziczą po ReportRequest:

- DateFrom, DateTo: wymagane DateOnly, obie granice włącznie i nie wcześniejsze niż
  `1970-01-01`; `DateFrom <= DateTo`. Wspólna metoda `ReportsService.Validate`
  sprawdza pełne daty dla wszystkich czterech raportów, także przy pustym wyborze kategorii.
- Scope: wymagany ExpenseAggregationScope.Personal albo Group.
- GroupId: null dla Personal, niepusty Guid dla Group.
- CategoryIds: nie-null lista Guid, domyślnie pusta; pusta lista oznacza pusty wynik.

Nie ma limitu liczby kategorii ani długości zakresu. Guid.Empty, nieznany scope,
sprzeczne scope/groupId, daty sprzed `1970-01-01` i odwrócone daty są odrzucane
przez ValidationException (HTTP 400 ValidationProblemDetails z nazwą pola).
Nie ma osobnych ograniczeń roku ani biznesowej górnej granicy dat; obowiązuje
wyłącznie zakres typu .NET DateOnly. Zakresy wieloletnie są akceptowane.
Actor jest osobnym argumentem z ICurrentUser na granicy uwierzytelnienia;
request nie zawiera ID użytkownika. ReportsReadException zachowuje kod i błędy
walidacji źródła. Anulowanie przechodzi do odczytów.

## Metody i wyniki

| Metoda IReportsService | Request | Response |
| --- | --- | --- |
| GetCategoryBreakdownAsync | CategoryBreakdownRequest | CategoryBreakdownResponse: Items (CategoryId, Name, TotalAmount) |
| GetExpenseTotalsByPeriodAndCategoryAsync | ExpenseTotalsByPeriodAndCategoryRequest | ExpenseTotalsByPeriodAndCategoryResponse: Granularity, Series (CategoryId, Name, Items: Period, TotalAmount) |
| GetExpenseTotalsByDayOfWeekAsync | ExpenseTotalsByDayOfWeekRequest | ExpenseTotalsByDayOfWeekResponse: Items (DayOfWeek, AverageAmount) |
| GetDailyExpenseTotalsAsync | DailyTotalsRequest | DailyTotalsResponse: Items (Date, TotalAmount) |

Kwoty pozostają decimal (liczby JSON). CategoryId=null i Name="Pozostałe"
identyfikują połączoną pozycję/serię. Nie jest to rzeczywista kategoria Expense.

Koło: do 8 obecnych kategorii osobno; powyżej 8 największych i suma pozostałych.
Wynik jest sortowany malejąco po TotalAmount, z deterministycznym rozstrzyganiem
remisów po CategoryId. Znaki kwot są zachowane.

Okresy: granularity dobiera Reports, bez argumentu interwału od klienta.
Liczba miesięcy to (DateTo.Year - DateFrom.Year) * 12 + DateTo.Month -
DateFrom.Month + 1. Liczymy miesiące kalendarzowe objęte zakresem, także
niepełne miesiące skrajne: do 24 Month, od 25 do 48 Quarter, od 49 Year.
Np. 2025-01-15–2026-12-31 to 24 miesiące (Month), a zakres kończący się
2027-01-01 to już 25 (Quarter). Nie używamy przybliżeń 30/365 dni.

Period wskazuje pierwszy dzień miesiąca, kwartału albo roku; pierwszy okres
może zaczynać się przed DateFrom, ale Expense sumuje tylko wybrany zakres.
Top 8 wybieramy raz według sum z całego zakresu, z tym samym zestawem serii
we wszystkich okresach. Pozostałe sumujemy osobno per okres. Brakujące okresy
każdej serii mają zera. Znane wybrane kategorie bez zakupów również są seriami
zerowymi; nieistniejące ID nie tworzą kategorii. Pusta lista wyboru daje pustą serię.

Dni tygodnia: wynik od Monday do Sunday. Dzielnikiem jest liczba wszystkich
wystąpień dnia tygodnia w zakresie, również bez wydatków. Dni niewystępujące
w krótkim zakresie mają średnią zero. AverageAmount nie jest zaokrąglany
do grosza; precyzję wyświetlania wybiera klient.

Daty: pełny włączny zakres z zerami dla brakujących dni, chronologicznie,
bez ograniczenia do miesiąca lub roku. Nawigacja kalendarza należy do frontendu.

## Warstwa HTTP

Wszystkie operacje są GET i wymagają uwierzytelnienia. Stare trasy
`monthly-totals` i `spending-trend` zostały zastąpione; nie mają aliasów.

| GET /api/reports/ | Aktualna metoda serwisu |
| --- | --- |
| category-breakdown | GetCategoryBreakdownAsync |
| period-category-totals | GetExpenseTotalsByPeriodAndCategoryAsync |
| day-of-week-totals | GetExpenseTotalsByDayOfWeekAsync |
| daily-totals | GetDailyExpenseTotalsAsync |

Parametry to dateFrom, dateTo, scope, groupId i categoryIds, a nie dawne
from/to, year/month czy categoryId. Scope przyjmuje personal/group, daty
YYYY-MM-DD. Scope podaje się dokładnie raz, małymi literami; wartości liczbowe
enumów nie należą do kontraktu. GroupId, jeśli występuje, musi być jednym
niepustym UUID w zapisie D (36 znaków). Personal wymaga pominięcia GroupId;
Group wymaga GroupId. Dostęp egzekwuje dotychczasowa ścieżka Expense → Grouping,
z zachowaniem reguł powiązanych historycznych wydatków.

CategoryIds korzysta ze standardowego wiązania kolekcji Guid w ASP.NET Core.
Wspólne DTO inicjalizuje `IReadOnlyList<Guid>` przez `new List<Guid>()`, którą
standardowy binder może uzupełnić, a kontroler używa `[FromQuery]`.
Nie ma własnego bindera ani walidacji pustych wartości w warstwie HTTP:

| Query string | Znaczenie |
| --- | --- |
| `categoryIds=UUID1` | Jedna kategoria |
| `categoryIds=UUID1&categoryIds=UUID2` | Dokładnie wskazane kategorie |
| `categoryIds=` | 400 — standardowe wiązanie nie konwertuje pustej wartości na Guid |
| Brak parametru | 200, pusta lista i pusty raport; nigdy wszystkie kategorie |
| `categoryIds=&categoryIds=UUID` lub kilka pustych wartości | 400 |
| Błędny UUID, Guid.Empty, białe znaki lub CSV | 400 |
| `categoryIds[0]=UUID1&categoryIds[1]=UUID2` | Standardowy zapis indeksowany kolekcji, 200 |

Formaty UUID obsługuje standardowy konwerter Guid; Guid.Empty nadal odrzuca
walidacja serwisu Reports. Weryfikacja uwierzytelnienia i dostępu do grupy
obowiązuje także dla pustego wyboru. Poprawny pusty wybór zwraca `items: []`
lub `series: []` z informacją o granularity w raporcie okresowym.

Przykłady na syntetycznych UUID (ten sam zestaw parametrów działa dla każdej trasy):

```http
GET /api/reports/category-breakdown?dateFrom=2026-11-01&dateTo=2026-11-30&scope=personal&categoryIds=30000000-0000-0000-0000-000000000001
GET /api/reports/period-category-totals?dateFrom=2025-01-15&dateTo=2027-01-31&scope=group&groupId=20000000-0000-0000-0000-000000000001&categoryIds=30000000-0000-0000-0000-000000000001&categoryIds=30000000-0000-0000-0000-000000000010
GET /api/reports/day-of-week-totals?dateFrom=2026-11-01&dateTo=2026-11-30&scope=personal
GET /api/reports/daily-totals?dateFrom=2025-01-01&dateTo=2026-12-31&scope=personal&categoryIds=30000000-0000-0000-0000-000000000001
```

OpenAPI hosta ma dedykowany transformer Reports: te same pięć parametrów,
scope jako enum tekstowy, categoryIds jako form/explode (powtarzany parametr).
Schema categoryIds to opcjonalna tablica UUID. Przykłady pokazują jeden UUID,
kilka UUID i pusty wybór (`[]` — pomiń parametr, zamiast wysyłać pusty ciąg).
Modele odpowiedzi wynikają z DTO i ich opisów: `categoryId: null` jednoznacznie
oznacza „Pozostałe”; nie rozpoznawaj tej pozycji po nazwie. Granularity i DayOfWeek
są tekstami JSON. Publiczne API nie przyjmuje granularity ani actor.

Pełne syntetyczne przykłady odpowiedzi są zapisane w jednej kolekcji
`postman/collections/4Money.Reports.postman_collection.json` wraz z przykładami
personal/group, pustego wyboru, top 8/Pozostałe oraz progów 24/25/48/49 miesięcy.

Kontroler wymaga uwierzytelnienia i ICurrentUser, zwraca Cache-Control: no-store
oraz ProblemDetails: 400 (parametry), 401 (tożsamość), 403 (odmowa), 404 (zasób),
409 (konflikt), 503 (niedostępność źródła). Szczegóły awarii nie są ujawniane.

## Weryfikacja

Końcowa weryfikacja 2026-11-18 po refaktoryzacjach: Reports.UnitTests 182/182,
Reports.IntegrationTests 19/19, Expense.UnitTests 67/67 i
Expense.IntegrationTests 122/122, bez błędów i pominięć, na izolowanym PostgreSQL 15.
Pełny host API potwierdził wszystkie 17 definicji GET kolekcji Postman,
standardowe wiązanie kategorii, produkcyjne OpenAPI, minimum dat i długi zakres
1970–2060. [Raport weryfikacji](REPORTS_FINAL_VERIFICATION.md) zawiera
zakres, zmiany, wyniki regresji i ograniczenia.

2026-11-18 po zmianie wspólnej walidacji dat: cała solucja Release
(`--no-restore --disable-build-servers -m:1`) bez błędów i ostrzeżeń.
Reports.UnitTests 182/182 (w tym HTTP/OpenAPI wszystkich czterech tras),
Expense.UnitTests 67/67, bez pominięć. Sprawdzono odrzucenie dat sprzed
1970-01-01 i odwróconego zakresu, akceptację minimum, zakresów wieloletnich
oraz górnej granicy DateOnly. Testy integracyjne dostosowano i skompilowano,
ale ich nie wykonywano. Nie uruchamiano PostgreSQL ani Dockera.
JSON kolekcji Postman i zapisanych odpowiedzi oraz `git diff --check` poprawne.

2026-11-18 po przejściu na standardowe model binding: cała solucja Release
(`--no-restore --disable-build-servers -m:1`) bez błędów i ostrzeżeń.
Reports.UnitTests 133/133, w tym osobno uruchomione 109/109 testów HTTP/OpenAPI;
Expense.UnitTests 67/67, bez pominięć. HTTP sprawdza wszystkie cztery trasy:
brak kategorii (pusty raport), jeden/powtarzane/indeksowane UUID, `categoryIds=`,
`abc`, oba porządki mieszanych pustych/UUID oraz wiele pustych wartości.
Puste wartości zwracają standardowe 400; nie ma własnej obsługi tych wartości.
Expense wymagało wyłączenia loggera Windows EventLog zmienną procesu
`Logging__EventLog__LogLevel__Default=None` po odmowie dostępu do dziennika.
Testy integracyjne dostosowano i skompilowano, ale ich nie uruchamiano.
Nie uruchamiano PostgreSQL, Dockera ani usług zewnętrznych.

2026-11-17: Reports.UnitTests 129/129 i Reports.IntegrationTests 18/18
w Release, bez pominięć. Integracje wykonały rzeczywiste zapytania Expense
i kontrolę dostępu z PostgreSQL 15 w odizolowanym kontenerze Docker.
Sprawdzono puste/błędne/mieszane kategorie, scope/groupId, progi okresów,
Pozostałe, średnie, zera, lata 1/9999, rok przestępny i przełom roku.

Osobny test uruchamia pełny host API/Kestrel z produkcyjnym DI, logowaniem
seed_anna, prawdziwym JWT i sprawdzaniem sesji. Wykonuje wszystkie 17 definicji
GET kolekcji Postman przez HttpClient, generuje dokument OpenAPI i potwierdza
401 po wylogowaniu. Nie jest to uruchomienie Postman CLI; zapisane odpowiedzi
w kolekcji pozostają syntetycznymi przykładami. Nie uruchamiano OCR/S3/SMTP/Tink.
Baza aplikacji i jej konfiguracja nie są używane przez te testy.
Szczegóły uruchomienia i granic testów: [tests/README](../backend/ZPI4M.Backend/tests/README.md#reports).
