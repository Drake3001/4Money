# Stan implementacji i kwestie otwarte

## Podstawa i sposób czytania

Bieżący opis obejmuje rebase Limits na master `860f421`, integrację agregacji
Expense oraz niezależne Custom i jedną migrację AddLimits z 2026-11-19.
Podstawa: kod, rejestracje hosta, konfiguracja, migracje i testy; zakres wykonanej
weryfikacji jest w ostatniej sekcji.

Ten plik odpowiada na pytania „co działa w kodzie?” i „czego brakuje?”, nie
zastępuje wymagań [produktu](product.md), reguł [domeny](domain.md) ani README
modułów. Nie dopisuj tu dziennika każdej sesji. Aktualizuj właściwy wiersz,
pozostawiając krótki opis ograniczenia, podstawę i zakres faktycznej kontroli.
Sukces testu z atrapą nie potwierdza prawdziwego dostawcy.

## Bieżące możliwości

| Obszar | Zaimplementowany przepływ | Istotna granica / brak |
| --- | --- | --- |
| User | Rejestracja i potwierdzenie, login/e-mail, sesje, Google, profil/ikony, reset hasła, zmiana e-maila i dezaktywacja | Brak zmiany hasła przez aktualne hasło, reaktywacji, usuwania danych i łączenia Google po e-mailu |
| Grupa startowa | Trigger po INSERT User tworzy zwykłą grupę PLN i członkostwo administratora w tej samej transakcji | Bez backfillu istniejących kont; nie jest szczególnym typem grupy |
| Grouping | Grupy/ikony/archiwizacja, zaproszenia, członkostwa, CRUD rozliczeń, agregaty i sugestie | Brak odbudowy/uzgadniania agregatów i nowych bankowych źródeł rozliczeń |
| Expense | Kategorie, historia, szczegóły, preview, create/edit/delete, pozycje i udziały z wersją | PLN; podział całego wydatku, nie produktów; preview nie rezerwuje źródła |
| Expense / Banking | Claim/release BankTransactionAssignment w transakcji Expense z saldami | Pole Settlement.BankTransactionId nie oznacza wdrożonego przypisywania przez Grouping |
| Receipt / OCR | Analiza pliku bez zapisu, podpisany wynik, zapis z Expense, odczyt paragonu i URL, usuwanie | PostgreSQL/S3 nie są atomowe; brak ogólnego workera uzgadniania plików |
| Worker | FastAPI, RapidOCR i model vision przez OpenRouter, korekty nazw/kategorii | Wynik wymaga weryfikacji użytkownika; brak dowodu spełnienia progów jakości dla bieżącego modelu |
| Banking | Próby/połączenia Tink, odczyty lokalne, worker refresh, paginacja i rejestr przypisań | Brak odłączania banku i odnawiania zgód; flaga konfiguracji nie potwierdza uprawnień dostawcy |
| Notifications | NotifyAsync, aktywni odbiorcy, inbox, odczyt pojedynczy/zbiorczy; zdarzenia Grouping i alerty Limits | Push poza zakresem; nieaktywne konta nie dostają nowych przypisań |
| Limits | Osobiste/grupowe definicje PLN, kalendarz Warsaw i niezależne nakładające się Custom=4, wykorzystanie, wersje, wykluczenia kalendarzowych, alerty i sweep | Brak webowego zarządzania budżetami; brak benchmarku długich zakresów; zużycie korzysta ze wspólnych agregacji SQL Expense |
| Reports | Cztery raporty HTTP przez IExpenseAggregationService z agregacjami SQL Expense | Brak raportowania w UI; kontrakt i wcześniejsza weryfikacja w notes/REPORTS_API.md oraz notes/REPORTS_FINAL_VERIFICATION.md |
| Web | Konto/Google, grupy, zaproszenia, rozliczenia, Expense/OCR, Banking i inbox z rzeczywistym API | Brak ekranów Limits/Reports i produkcyjnego obrazu/hostingu; testy Node nie są testami przeglądarki |
| Mobile | Uzgodniony kierunek React + Capacitor | Brak projektu aplikacji mobilnej i weryfikacji na urządzeniu |

Frontend `src/mocks/` zawiera typy i pomocniki, nie symulator danych HTTP.
Bankowy pulpit, zależność biblioteki wykresów ani HTTP 501 nie są dowodem
ukończenia Reports. Lista braków nie upoważnia do rozszerzenia zakresu zadania.

## Niezgodności i pułapki wymagające uwagi

- Reguła UI „Zniżka” wymaga ujemnej ceny, ilości 1 i rabatu 0. Backend waliduje
  pozycje niezależnie od tej kategorii. Przed zmianą kontraktu ustal, czy reguła
  ma być egzekwowana także poza formularzem. Nie udawaj, że oba poziomy są identyczne.
- Materiały TXT i diagramy mogą opisywać wcześniejszy model. Aktualne FK nie
  zawsze są mapowane w EF; przy zmianach schematu sprawdzaj również SQL init.
  Obecność pola lub relacji nie zastępuje pełnego przepływu serwisowego.
- Limits działa po stronie API i generuje alerty; brak podłączenia frontendu
  nie oznacza braku usług. Sweep nie odnawia okresów. Aktualny kontrakt jest w
  [Limits](../backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md).
- Konteksty Expense/Grouping/Notifications/Receipt/Limits i scoped Banking
  współdzielą połączenie. Dołączenie do transakcji nadal jest jawne; User i
  fabryka Banking pozostają osobno. Reguły rollbacku są w [architekturze](architecture.md#transakcje-między-modułami).
- Ostatnia migracja to `20261119182900_AddLimits`, po
  `NormalizeExpenseCategoriesAndAddDeposit`, nie bezpośrednio po przypisaniach
  Banking. Pliki po rebase nie naprawiają już zastosowanego schematu starej bazy.
- Seed ma syntetyczne klucze storage i zachowuje kategorie zarządzane migracjami.
  Nie tworzy odpowiadających obrazów, nie odtwarza zgód Tink i nie potwierdza
  dostępności zewnętrznych usług. Reset nie jest migracją ani częścią testu dokumentacji.
- Uwierzytelnienie wymaga bazy i aktywnej sesji, nie tylko poprawnego JWT.
  UI w localStorage może zachować token, który po zmianie konta jest już nieważny.
- Podpisany URL S3, zaakceptowany mail SMTP i ukończona próba Tink dowodzą
  różnych etapów. Nie traktuj ich jako dowodu dostarczenia, istnienia pliku
  albo pełnego importu. Ograniczenia Google/SMTP opisuje [User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md).

## Otwarte prace i decyzje

Przy nowym zadaniu potwierdź potrzebę i priorytet: web Limits/Reports, aplikacja
mobilna, uzgadnianie agregatów, bankowe źródła rozliczeń, zarządzanie zgodami,
odłączanie banku, wdrożenie frontendu i pomiar jakości OCR. To nie jest obietnica
ich realizacji ani lista automatycznych blockerów wszystkich zmian.

Rozszerzenie walut, podziału produktów i reguł nowych źródeł finansowych wymaga
osobnego ustalenia. Obecne zaokrąglanie udziałów i progi alertów mają już kontrakty;
nie oznaczaj ich ponownie jako całkowicie nierozstrzygniętych. Automatyczne
powiązanie paragon/bank i push pozostają rozszerzeniami, nie brakami bieżącego inboxa.

Przed wdrożeniem wieloinstancyjnym uwzględnij lokalny magazyn Google challenge,
lokalne limity IP i brak trwałego outboxa/cleanup workera. Nie deklaruj gotowości
produkcyjnej na podstawie samych testów lokalnych. Rzeczywiste Google, SMTP,
Tink, OpenRouter/OCR i S3 wymagają odrębnej kontroli z bezpiecznymi danymi.

## Ostatnia weryfikacja backendu

2026-11-19, niezależne Custom i konsolidacja migracji do
`20261119182900_AddLimits`: Release build całej solucji bez błędów i ostrzeżeń,
style/whitespace bez zapisu i `git diff --check` zaliczone. Regeneracja designera
oraz snapshotu względem wspólnego modelu potwierdziła ten sam końcowy model EF;
migracje mastera pozostały bez zmian. Cztery wykluczenia SQL dotyczą tylko
włączonych okresów 1/2/3. Nie ma automatycznego wyłączania innych limitów.

Wykonano **1272 testy, 0 błędów, 0 pominięć**: wszystkie 12 śledzonych projektów
jednostkowych oraz integracyjne Limits (33/33), Expense (128/128) i Banking
(152/152). Limits.UnitTests: 82/82. Po poprawieniu scope'ów w nowym scenariuszu
create/edit/delete ponowiono cały Limits.IntegrationTests; podana suma uwzględnia
wyłącznie końcowe wyniki poszczególnych zestawów, bez podwójnego liczenia.

Sprawdzono współistnienie i ponowne włączanie identycznych Custom przy limicie
kalendarzowym, niezmienność pozostałych wersji/flag, niezależne sumy i alerty,
deduplikację per definicja oraz rollback wcześniejszych alertów wraz z wydatkiem.
HTTP osobistych/grupowych, SQL poza serwisami i współbieżne tworzenie potwierdzają
nową regułę. Zachowano walidację dat, uprawnienia, zamrożenie zakończonych Custom
i konflikty kalendarzowych. Testy migracji obejmują pusty Up/Down/Up, odmowę przy
danych w każdej zmienianej tabeli, zachowanie wcześniejszego schematu/danych
oraz zgodność runtime/snapshotu. SQL init → migracje → seed i ponowienie seedera
przeszły; seed ma 8 grupowych i 6 osobistych definicji oraz puste alert-state.

Raporty TRX: `/tmp/zpi-custom-unit-all/` i `/tmp/zpi-custom-results/`
(końcowy Limits: `limits-integration-final.trx`). Pozostałych integracji,
frontendu i pytest nie uruchamiano; nie weryfikowano dostawców zewnętrznych.
Jednorazowy PostgreSQL 15 służył wyłącznie testom. Nie zmieniano bazy aplikacji,
nie konwertowano istniejących limitów ani historii EF.
Polecenia kontroli: [development](development.md#kontrole-backendu).
