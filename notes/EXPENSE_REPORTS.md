# Expense — kontrakt odczytu dla Reports

Stan: 2026-11-08. Serwis wewnętrzny `IExpenseReadService`, rejestrowany przez
`AddExpenseServices`. Reports korzysta z interfejsu i DTO w
`Expense.Services.Contracts.Reports`; nie pobiera encji ani repozytoriów Expense.
Nie dodano endpointów, migracji ani zależności Expense → Reports.

## Operacje

| Metoda | Wynik |
| --- | --- |
| GetExpensesAsync | PagedResult<ExpenseReportEntry>: lista zakupów, grupy/płatnicy jako ID, data, nazwa, sprzedawca, kwota, koszt użytkownika, kategorie, oznaczenia źródeł |
| GetTotalsAsync | Liczba wydatków, wartość zakupów, koszt użytkownika i kwota przez niego zapłacona |
| GetCategoryTotalsAsync | Te same wartości w podziale na kategorie, z ID i nazwą kategorii |
| GetTimelineAsync | Te same wartości per dzień/miesiąc/rok, opcjonalnie również per kategoria |

Kwoty są typu decimal, daty DateOnly, obecnie PLN. DTO nie zawierają plików,
odpowiedzi OCR, danych bankowych ani encji EF. Nazwy grup i osób Reports może
uzyskać przez publiczne kontrakty odpowiednich modułów.

## Zakres i dostęp

Każda metoda przyjmuje `actor` — ID uwierzytelnionego wywołującego.
`Filter.UserId` określa osobę, której koszt i kwotę zapłaconą liczymy;
domyślnie jest to actor. Nie jest to filtr listy ani przyznanie uprawnień.

Obowiązują reguły odczytu historii Expense: aktualny członek widzi zakupy
swoich aktywnych grup, a historycznie płatnik i uczestnik zachowują dostęp
do powiązanych wydatków. Filtr innej osoby nie poszerza tego zakresu.
Niedostępna wskazana grupa zwraca NotFound, pusty actor — Forbidden.

Bez GroupId i PayerId domyślny ParticipantId to UserId (lub actor).
To daje listę własnych kosztów ze wszystkich dostępnych grup.
Podanie GroupId bez ParticipantId obejmuje wszystkie dostępne zakupy tej grupy.
PayerId bez ParticipantId pozwala odczytać także zakupy opłacone za innych,
w których płatnik nie ma udziału. Jawny ParticipantId ma pierwszeństwo.

## Filtry

Wspólny ExpenseReportFilter: UserId, GroupId, ParticipantId, PayerId,
DateFrom, DateTo, MinAmount, MaxAmount, CategoryId, Search, HasReceipt,
HasBankTransaction.

- Obie daty są **włącznie**, także gdy są równe. DateTo obejmuje cały dzień UTC,
  również starsze wydatki zapisane z godziną. DateOnly.MaxValue jest obsługiwane.
  To kontrakt raportów; istniejący endpoint historii zachowuje koniec wyłączny.
- MinAmount/MaxAmount dotyczą pełnej kwoty wydatku, są włącznie i wymagają
  nieujemnych wartości z dokładnością do grosza.
- Search przeszukuje nazwę i sprzedawcę bez rozróżniania wielkości liter.
  Znaki %, _ i ! są traktowane dosłownie; limit długości wynosi 200.
- CategoryId wybiera wydatki zawierające pozycję tej kategorii.
  **Lista zwraca cały wydatek i wszystkie jego kategorie. Agregaty z tym filtrem
  sumują wyłącznie pozycje wybranej kategorii**, także na osi czasu.
  Przykład: zakup 100 zł, w tym żywność 60 zł. Lista po kategorii żywność zwraca
  zakup z Amount=100, a GetTotalsAsync z tym filtrem zwraca Amount=60.
- Brak wyników daje pustą listę/serię lub zerowe sumy.
- Błędny zakres, puste identyfikatory, niepoprawne enumy i paginacja zwracają
  InvalidExpense z ValidationErrors. Nie są normalizowane do innych wartości.

## Sortowanie i paginacja

ExpenseReportQuery zawiera Filter, PageNumber (od 1), PageSize (1–100),
SortBy (Date, Amount, Name, Merchant) i Descending. Domyślnie data malejąco.
Remisy rozstrzyga ID rosnąco. Sortowanie tekstu korzysta z porządku PostgreSQL;
brak sprzedawcy sortuje się jak SQL NULL.

Kategorie można sortować po Amount, UserShareAmount, UserPaidAmount lub Name,
w obu kierunkach. Domyślnie Amount malejąco; remisy rozstrzyga CategoryId.
Nazwy kategorii sortowane są ordinal. Dane w czasie są zawsze chronologiczne,
a kategorie w okresie według ID. Puste okresy nie są dopisywane;
Reports może je uzupełnić zerami na potrzeby wykresu.

Agregaty nie mają paginacji i obejmują cały przefiltrowany zbiór.
Count listy jest osobnym zapytaniem; przy równoległych zapisach może różnić
się od stanu strony. Dane pojedynczego wydatku, pozycji i udziałów raportu
są odczytywane w jednym zapytaniu SQL, bez śledzenia EF.

## Znaczenie kwot

- Amount: wartość zakupów, każdy wydatek liczony raz.
- UserShareAmount: koszt użytkownika z udziału, obliczany istniejącym
  ExpenseCalculationService, z identycznym rozdzielaniem groszy jak zapis i preview.
- UserPaidAmount: wartość zakupów, dla których UserId jest płatnikiem,
  niezależnie od jego udziału.
- ExpenseCount: liczba wydatków w danym zbiorze, kategorii lub okresie.
  Jeden zakup może należeć do kilku kategorii, więc liczby per kategoria
  nie muszą sumować się do liczby wszystkich zakupów.

Kategorie sumujemy z kwot pozycji: cena × ilość zaokrąglone do grosza
AwayFromZero, następnie rabat. Uwzględniamy wartości ujemne, zerowe i systemowe
„Nieznane”. Koszt użytkownika w kategorii odpowiada jego procentowi całego
wydatku. Zaokrąglenia kategorii są uzgadniane do rzeczywistej kwoty udziału
użytkownika metodą największych reszt; remisy rozstrzyga CategoryId ordinal.
Suma jego kategorii zawsze równa się jego udziałowi w wydatku.
Rozdzielenie groszy odbywa się per wydatek, przed sumowaniem okresów.

Settlement nie jest zakupem i nie wchodzi do zestawień. Nie doliczamy oddzielnie
powiązanej transakcji bankowej lub paragonu — reprezentują źródła tego samego zakupu.

## Przykłady dla Reports

```csharp
// Moje koszty w kategoriach za cały listopad.
var filter = new ExpenseReportFilter
{
    DateFrom = new DateOnly(2026, 11, 1),
    DateTo = new DateOnly(2026, 11, 30)
};
var categories = await expenses.GetCategoryTotalsAsync(actor, filter,
    ExpenseCategorySortBy.UserShareAmount, cancellationToken: token);

// Miesięczny wykres kategorii dla użytkownika w jednej grupie.
var timeline = await expenses.GetTimelineAsync(actor, new ExpenseTimelineQuery
{
    Filter = new ExpenseReportFilter
    {
        GroupId = groupId,
        UserId = actor,
        DateFrom = new DateOnly(2026, 1, 1),
        DateTo = new DateOnly(2026, 12, 31)
    },
    Period = ExpenseReportPeriod.Month,
    ByCategory = true
}, token);
```

Raport osobisty powinien przedstawiać UserShareAmount, a raport zakupów grupy
Amount. Słupki „zapłaciłem” używają UserPaidAmount.
Serwis dostarcza dane; wybór wykresu, prezentacja i wykorzystanie limitów należą
do Reports. Bieżące ograniczenie: agregaty materializują pasujące wydatki,
pozycje i udziały, żeby zachować obliczenia groszy; nie są przetestowane obciążeniowo
na wieloletnich zbiorach produkcyjnych.
