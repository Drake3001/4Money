# Końcowa weryfikacja Expense i Reports — 2026-11-18

Zakres: lokalna gałąź Reports, HEAD `baf150e`, cały diff względem `origin/master`
oraz poprawki opisane poniżej. Repozytorium przed weryfikacją było czyste.
Nie wykonano commita, pusha ani merge'a.

## Wynik końcowy

PR jest gotowy do pusha w zweryfikowanym zakresie. Nie znaleziono błędu
w działaniu Expense ani Reports; pozostałością refaktoryzacji był jeden
nieużywany typ, usunięty przed końcowym buildem i regresją.

Cała solucja Release: **0 błędów, 0 ostrzeżeń**.
Wszystkie **17 projektów: 1277 zaliczonych, 0 niezaliczonych, 0 pominiętych**.
Wyniki pochodzą z jednego końcowego przebiegu po zmianach kodu.

| Moduł | UnitTests | Tests (kontrakty/HTTP) | IntegrationTests |
| --- | ---: | ---: | ---: |
| Banking | 121 | 95 | 152 |
| Expense | 67 | — | 122 |
| Grouping | — | 46 | — |
| Notifications | 17 | 19 | 13 |
| Receipt | 108 | — | 5 |
| Reports | 182 | — | 19 |
| Shared | 49 | — | — |
| User | 72 | 101 | 89 |
| **Razem** | **616** | **261** | **400** |

W każdym projekcie: 0 błędów i 0 pominięć. Testy HTTP/OpenAPI Reports należą
do UnitTests; nazwy kolumn odpowiadają nazwom projektów, nie zastępują opisu
użytych zależności. Expense łącznie **189/189**, Reports **201/201**.

Potwierdzono cztery endpointy na pełnym hoście, wszystkie 17 definicji GET
Postmana przez HttpClient oraz zgodność produkcyjnego OpenAPI.
Wykonane żądania potwierdzają 400 dla pustych i mieszanych wartości kategorii;
brak parametru daje pusty raport. Test rzeczywistego S3 również przeszedł.
Statyczna kontrola Postmana i `git diff --check` (zmiany lokalne oraz cały diff
względem `origin/master`) zakończyły się powodzeniem.

Po testach PostgreSQL nie zawierał żadnej bazy fixture. Zatrzymano i usunięto
oba kontenery testowe oraz ich anonimowe wolumeny, również wolumen po nieudanym
starcie testowego S3. Końcowa kontrola Dockera nie wykazała tych zasobów.
Nie zatrzymywano ani nie usuwano zasobów aplikacji.

## Przegląd kodu i pokrycia

- `IExpenseAggregationService` i `ExpenseAggregationType` są używane konsekwentnie.
  Agregacja korzysta z istniejącego `IExpenseRepository` i scoped DI.
  Nie pozostały odwołania do ExpenseRead, poprzedniego interfejsu Reports,
  usuniętego repozytorium agregacji ani własnego bindera kategorii.
- Porównano CTE `AllocatedAmounts` i listę parametrów Npgsql z wersją sprzed
  przeniesienia repozytorium (`4837a1d^`). Treść SQL i parametry zachowano;
  zmieniła się nazwa enuma zakresu. Do SQL interpolowane są wyłącznie
  stałe wyrażenia dobrane przez enum, a dane wejściowe są parametryzowane.
- Personal korzysta z udziałów; Group z pełnych kwot dostępnych zakupów grupy.
  SQL rozdziela grosze na całym wydatku przed filtrowaniem kategorii.
  Istniejące testy finansowe używają jawnych kwot oczekiwanych i obejmują
  rabaty, ujemne pozycje, ilości ułamkowe, wiele kategorii i nietypowe grosze.
- Testy raportów obejmują top 8/Pozostałe, sortowanie i zachowanie sum,
  wspólne serie oraz brakujące okresy, progi 24/25 i 48/49 miesięcy
  kalendarzowych z częściowymi granicami, średnie wszystkich wystąpień dni
  tygodnia, zera, rok przestępny, daty UTC i górną granicę DateOnly.
- HTTP używa standardowego wiązania kolekcji Guid. Brak kategorii daje
  pusty raport, a `categoryIds=` i mieszane puste/UUID wartości dają 400.
  Testy sprawdzają wszystkie cztery trasy, scope/groupId, ICurrentUser,
  brak dostępu do cudzych grup, historię byłego członka, błędy ProblemDetails
  i wspólną minimalną datę 1970-01-01.
- Przegląd zmian nie wykazał zmian niezwiązanych z funkcją raportowania
  ani nowych zależności produkcyjnych. Nie zmieniono reguł biznesowych,
  schematu bazy ani obliczeń finansowych.

## Zmiany wykonane podczas weryfikacji

- Rozszerzono istniejący
  `ReportsProductionHostTests.ProductionHostServesOpenApiAndAllPostmanRequestsWithRealJwt`.
  Nowe asercje sprawdzają parametry rzeczywistego OpenAPI, oba porządki
  pustej wartości i UUID, odrzucenie obu dat sprzed minimum także bez
  kategorii oraz zakres 1970–2060 na wszystkich czterech trasach.
  Dla kalendarza sprawdzana jest liczba dni i obie daty brzegowe;
  dla okresów agregacja Year i 91 okresów. Uzupełnia to dotychczasowe
  testy walidacji z atrapami o produkcyjny host, JWT i PostgreSQL.
- Usunięto nieużywany `ExpenseAggregationParameters`: wyszukiwanie całego
  repozytorium wykazało wyłącznie jego deklarację, bez konsumentów.
  Nie zmieniono aktywnych interfejsów ani DTO HTTP.
- Poprawiono liczbę projektów testowych (17), nieaktualny brak API Reports
  i zduplikowane, sprzeczne wiersze statusu. Wyniki tej sesji są oddzielone
  od historycznych przebiegów.
- Nie usuwano ani nie osłabiano istniejących testów.

## Środowisko i polecenia

Release build:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
dotnet build ZPI4M.Backend.slnx --configuration Release --no-restore --disable-build-servers -m:1
```

Regresja po końcowych zmianach kodu:

```powershell
dotnet test ZPI4M.Backend.slnx --configuration Release --no-build --no-restore --disable-build-servers -m:1 --logger trx --results-directory ../../.cache/backend-results/final-reports-20261118
```

Testy mają włączone `TEST_POSTGRES_CONNECTION_STRING` do osobnego PostgreSQL
15-alpine oraz `STORAGE_INTEGRATION_TESTS=1` do osobnego SeaweedFS 4.48.
Konfiguracja jest syntetyczna, a każda próba PostgreSQL tworzy własną bazę.
Windows EventLog jest wyłączony wyłącznie zmienną procesu testowego
`Logging__EventLog__LogLevel__Default=None`.
Nie korzystano z baz ani magazynu aplikacji.

Logi lokalne: `.cache/reports-release-build-final.log`,
`.cache/reports-full-regression.log` i pliki TRX w katalogu wyników powyżej.
Logi i konfiguracja testowa nie są dodawane do PR.

## Granice weryfikacji

Żądania kolekcji Reports są wykonywane przez HttpClient w rzeczywistym hoście
Kestrel, z produkcyjnym DI, logowaniem, JWT, sesją i PostgreSQL. Nie uruchamiano
Postman CLI; kolekcja celowo zatrzymuje Runner po każdym żądaniu.
Statyczna kontrola obejmuje wszystkie sześć kolekcji: poprawny JSON,
składnię 217 skryptów, JSON 21 zapisanych odpowiedzi oraz 17 aktualnych
definicji GET Reports. Zapisane odpowiedzi pozostają syntetycznymi przykładami.

Nie testowano rzeczywistych usług Tink, Google, OCR ani dostarczania SMTP.
Nie wykonywano testów obciążeniowych na wieloletnim zbiorze produkcyjnym;
zaakceptowanie długiego zakresu dat nie jest pomiarem skalowalności.
Testy frontendu i workera Python pozostają poza zakresem tej regresji backendu.
