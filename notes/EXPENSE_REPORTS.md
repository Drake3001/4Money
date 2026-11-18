# Expense — kontrakt odczytu dla Reports

## Dedykowane agregacje SQL — 2026-11-17

`Services/Aggregation/ExpenseAggregationService` implementuje `IExpenseAggregationService`.
Serwis jest rejestrowany scoped przez `AddExpenseServices` i korzysta z istniejącego
`IExpenseRepository.GetAggregatedExpenseTotalsAsync` (scoped w hoście).
Od refaktoryzacji 2026-11-18 implementacja jest w `Repositories/ExpenseRepository/ExpenseRepository.cs`,
a SQL pozostaje w osobnym `Repositories/ExpenseRepository/ExpenseAggregationQueries.cs`.
Typy `ExpenseAggregateRow` i `ExpenseAggregationType` są w tym samym katalogu.
Przeniesienie nie zmienia SQL, parametrów, wyników ani reguł agregacji.
Ocena przejścia na LINQ z 2026-11-18 pozostawiła SQL ze względu na zmierzoną
regresję wariantu z korelacjami; [przebieg i ograniczenia](EXPENSE_LINQ_EVALUATION.md).
Publiczny kontrakt pozostaje bez zmian; wszystkie metody przyjmują actor,
DateFrom/DateTo (włącznie), Scope, GroupId oraz jawne CategoryIds.

| Metoda | Wynik |
| --- | --- |
| GetCategoryBreakdownAsync | CategoryId, TotalAmount |
| GetExpenseTotalsByPeriodAndCategoryAsync | Period (pierwszy dzień miesiąca/kwartału/roku), CategoryId, TotalAmount; granularity podaje Reports |
| GetExpenseTotalsByDayOfWeekAsync | System.DayOfWeek, TotalAmount; poniedziałek–niedziela, bez średnich |
| GetDailyExpenseTotalsAsync | Date, TotalAmount; tylko dni z pasującymi pozycjami, także z sumą zero |

Personal liczy koszt actor z udziałów (również w grupach); Group pełne kwoty
zakupów wskazanej grupy w dostępnym zakresie. Płatnik bez udziału nie otrzymuje
kosztu osobistego. Puste CategoryIds zwraca pustą listę, nie wszystkie kategorie.
Wyniki są rzadkie: nie dopisujemy okresów, dni ani kategorii bez pasujących pozycji.
Nie grupujemy kategorii w „Pozostałe”, nie ograniczamy zakresu dat/liczby kategorii.

Repozytorium korzysta z parametryzowanego SQL przez EF Core `Database.SqlQueryRaw`.
Wspólne CTE filtrują zakres i dostęp, sumują zaokrąglone kwoty ExpenseItem
(round(cena × ilość, 2) + rabat) oraz wyliczają udziały i rozdział groszy
metodą największych reszt. SQL użyto ze względu na funkcje okienkowe potrzebne
do zachowania reguł `ExpenseCalculationService` oraz uzgodnionych zasad alokacji kategorii.
Remisy rozstrzygają UUID, tak jak ich tekstowy porządek ordinal w kalkulatorach.
W raporcie osobistym kategorie filtrujemy dopiero po rozdzieleniu groszy w całym
wydatku; wybór kategorii nie zmienia jej kwoty. Agregacja odbywa się w bazie,
do C# trafiają tylko końcowe agregaty. Nie pobieramy encji, OCR, plików ani banku.

Grouping udostępnia aktywne grupy przez istniejący `IGroupExpenseMetadataLookup`.
Ten port nie autoryzuje samodzielnie historii Expense: zachowujemy jej regułę
aktywna grupa LUB płatnik LUB uczestnik. Niedostępna wskazana grupa daje NotFound;
były członek zachowuje tylko powiązane zakupy. Reports odpowiada za walidację
requestów; Expense odrzuca sprzeczne argumenty wewnętrzne, aby nie rozszerzać
zakresu przez brak GroupId albo nieznany enum. Nie dodano zależności od wnętrza
Grouping ani Reports. Anulowanie przechodzi do odczytów.

Daty i okresy odnoszą się do UTC; skrajne okresy sumują tylko wybrany zakres.
Parametry dat nie korzystają z domyślnej konwersji MinValue/MaxValue na infinity
w Npgsql. Już zapisane timestampy -infinity/infinity są interpretowane jako
daty 0001-01-01/9999-12-31, zgodnie z odczytem .NET. Nie zmieniono schematu
ani istniejących zapisów. Reguły obliczeń zakładają
spójne wydatki i udziały utrzymywane przez istniejącą ścieżkę zapisu Expense.
Zapytania nie zostały zbadane obciążeniowo na wieloletnich zbiorach produkcyjnych.

## Usunięcie nieużywanego odczytu — 2026-11-18

Reports korzysta z `IExpenseAggregationService`; przygotowanie prezentacji opisuje
[kontrakt Reports](REPORTS_API.md). Starszy serwis listy, sum i osi czasu nie miał
konsumentów w aktywnych funkcjach ani kontrolerach. Usunięto jego implementację,
interfejs, rejestrację DI, prywatne DTO, kalkulator w pamięci oraz dwa odczyty
repozytorium materializujące encje. Usunięto również opcję włącznego końca daty
z zapytania historii, używaną wyłącznie przez ten serwis. Historia HTTP nadal ma
wyłączny koniec zakresu; włączne granice agregacji SQL pozostały bez zmian.

Zachowano testy bieżącej agregacji. Test finansowy dla obu uczestników porównuje
SQL z jawnymi kwotami dla czterech stałych zakupów, bez wywoływania starego serwisu:
Personal (Food/Cleaning) 5,52/1,67 i 3,50/0,34; Group 9,02/2,01.
Usunięto wyłącznie zestawy testów dawnego odczytu i jego kalkulatora.
Aktywne kontrakty agregacji, SQL, Reports, endpointy, historia i kalkulator udziałów
ExpenseCalculationService nie zmieniły działania.
