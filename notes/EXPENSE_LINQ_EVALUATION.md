# Expense — ocena zastąpienia SQL przez LINQ, 2026-11-18

## Decyzja

Pozostawiono działające `ExpenseRepository.GetAggregatedExpenseTotalsAsync` oraz
`ExpenseAggregationQueries.cs`. Badany wariant LINQ wykonywał obliczenia w PostgreSQL
i dawał zgodne wyniki na danych diagnostycznych, ale istotnie pogarszał czas wykonania
raportów osobistych. Nie wprowadzono go do kodu aplikacji. Nie zmieniono sygnatur,
kontraktów serwisów, Reports, endpointów, DI, SQL ani oczekiwanych wyników testów.

To nie jest twierdzenie, że algorytm jest matematycznie niewyrażalny w LINQ.
Nie uzyskano implementacji spełniającej łącznie wymagania dokładności i wydajności.

## Co sprawdzono

Użyto zależności projektu: EF Core 10.0.4 i Npgsql.EntityFrameworkCore.PostgreSQL
10.0.3. Projekty diagnostyczne utworzono poza repozytorium, w katalogu tymczasowym.
Odczyty aplikacji i jej dane nie były używane.

SQL wykonuje dwa etapy alokacji metodą największych reszt: udział całego wydatku
oraz rozdzielenie udziału między kategoriami. Każdy wymaga sumy podłóg i pozycji
w porządku malejącej reszty z remisem rozstrzyganym UUID. Kategorie muszą pozostać
kompletne aż do zakończenia drugiej alokacji. Niezależne zaokrąglenie udziałów lub
filtrowanie kategorii przed alokacją nie jest równoważną implementacją.

`OrderBy(...).Select((row, index) => ...)` nie tłumaczy się na SQL w aktualnym
providerze. EF Core nie udostępnia ogólnego LINQ do `ROW_NUMBER() OVER (...)` oraz
sum okienkowych; [obsługa funkcji okienkowych jest śledzona w EF Core](https://github.com/dotnet/efcore/issues/12747).
Nie oznacza to braku wewnętrznego użycia funkcji okienkowych przez translator EF.

Sprawdzono alternatywę opartą na `IQueryable`, `GroupBy`, `Sum`, `Math.Floor`,
`Math.Round(decimal, 2)` i skorelowanym `Count`. Pozycję od zera obliczano przez
zliczenie elementów tego samego wydatku z większą resztą albo równą resztą
i mniejszym UUID (`Guid.CompareTo` tłumaczy się na porównanie PostgreSQL `uuid`).
Kwotę zwiększano o grosz, gdy pozycja była mniejsza od liczby pozostałych groszy.
W drugim etapie korelacje operowały na sumach wszystkich kategorii wydatku;
filtr wybranych kategorii stosowano na końcu. Wykonano również uproszczenie
podzapytań, aby nie powtarzać zbędnych złączeń przy obliczaniu reszt.

`ToQueryString()` potwierdził translację wszystkich sześciu agregacji w obu
zakresach. PostgreSQL wykonał te zapytania; nie materializowano wydatków ani
pozycji do obliczeń w C#. Do klienta trafiały tylko końcowe agregaty.

## Porównanie z działającą metodą

Osobny kontener PostgreSQL 15 (`postgres:15-alpine`), bez wolumenów aplikacji,
zawierał bazę diagnostyczną z uproszczonymi trzema tabelami Expense i indeksami
na identyfikatorach wydatków. Utworzono 1000 syntetycznych wydatków, po dwóch
uczestników (33,33% i 66,67%) oraz osiem kategorii na wydatek. Dane zawierały
ujemną pozycję, rabat i ilość 0,5 z zaokrągleniem wartości pozycji. Kwota wydatku
była sumą zaokrąglonych pozycji. Użyto dat rozłożonych w 2026 r., aktywnej grupy
i sesji PostgreSQL w strefie Europe/Warsaw; daty raportu pozostały UTC.

Wyniki `SequenceEqual` z produkcyjną metodą SQL były zgodne dla Category, Month,
Quarter, Year, DayOfWeek i Day, zarówno Personal, jak i Group. Ten ograniczony
zbiór nie dowodzi pełnej zgodności LINQ dla wszystkich danych i skrajnych dat.

Po rozgrzaniu zapytań wykonano trzy dodatkowe porównania Personal/Category:

| Powtórzenie | Obecny SQL | Uproszczony LINQ |
| --- | ---: | ---: |
| 1 | 49,8 ms | 472,5 ms |
| 2 | 51,7 ms | 486,7 ms |
| 3 | 51,4 ms | 471,2 ms |

`EXPLAIN (ANALYZE, BUFFERS)` dla tych samych zapytań i danych:

- SQL: Execution Time 53,882 ms; wspólne CTE i `WindowAgg`.
- LINQ: Execution Time 513,166 ms; podzapytania skorelowane z powtarzaną agregacją
  pozycji, w tym skany po indeksie pozycji wykonywane 7984 razy.

To pomiar diagnostyczny, nie benchmark produkcyjny ani dowód kosztu każdej
możliwej implementacji LINQ. Wystarcza do odrzucenia badanego wariantu przy
wymaganiu zachowania wydajności. Brak materializacji w C# i pojedyncze polecenie
SQL nie gwarantują taniego planu wykonania. Częściowa zamiana tylko zakresu Group
nie realizowałaby żądanej refaktoryzacji całej metody.

## Weryfikacja pozostawionej implementacji

Pełne Expense.IntegrationTests wykonano na tym samym izolowanym serwerze,
korzystając z istniejącego fixture tworzącego i usuwającego osobne losowe bazy
`expense_tests_<guid>`: 128/128, bez pominięć. Testy obejmują porównanie z
istniejącymi kalkulatorami, udziały i grosze, ujemne pozycje, rabaty, wybór i pustą
listę kategorii, dostęp byłego członka, granice UTC, ostatnią mikrosekundę dnia,
lata 1/9999 oraz timestampy infinity. Nie zmieniano asercji ani kodu testów.

Wyniki kompilacji i testów jednostkowych podaje
[status implementacji](../agent-context/implementation-status.md).
Nie wdrożono nowej implementacji LINQ, więc nie należy przypisywać jej wyników
pełnego zestawu testów integracyjnych dotychczasowej metody SQL.
