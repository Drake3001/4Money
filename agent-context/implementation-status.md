# Stan implementacji i kwestie otwarte

## Podstawa i sposób czytania

Podstawą opisu są kod, rejestracje hosta, konfiguracja, migracje i zakres testów.
Obecność implementacji nie potwierdza działania środowiska serwerowego ani
rzeczywistych dostawców zewnętrznych.

Możliwości i braki nie zastępują wymagań [produktu](product.md), reguł
[domeny](domain.md) ani kontraktów modułów. Aktualizuj właściwy wiersz zamiast
dopisywać historię sesji.

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
| Reports | Cztery raporty HTTP przez IExpenseAggregationService z agregacjami SQL Expense | Brak raportowania w UI; kontrakt w [Reports](../notes/REPORTS_API.md) |
| Web | Konto/Google, grupy, zaproszenia, rozliczenia, Expense/OCR, Banking i inbox; produkcyjny obraz statyczny z Nginx | Brak ekranów Limits/Reports; hosting niepotwierdzony, testy Node nie są testami przeglądarki |
| Mobile | Uzgodniony kierunek React + Capacitor | Brak projektu aplikacji mobilnej i weryfikacji na urządzeniu |
| Konfiguracja | Wspólny env lokalnego Compose/API/EF, sekcje appsettings i nadpisania Compose; osobne env serwerowe APP/OCR | Prawidłowe sekrety i adresy są wymagane; szablony nie potwierdzają gotowości środowiska |
| Wdrożenie | deployment.yml: CI → cztery obrazy GHCR → klony VM APP/OCR przez Terraform → opcjonalne SSH/CD, efbundle przed aktualizacją API | Pierwsze wdrożenie niepotwierdzone; VM/env/GHCR/SSH i firewall wymagają przygotowania; dostęp początkowo przez Tailscale |

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
- Łańcuch migracji kończy AddLimits po NormalizeExpenseCategoriesAndAddDeposit.
  Osłony danych oraz postępowanie z rozbieżnym schematem opisuje
  [development](development.md#modele-i-migracje); seed nie jest naprawą migracji.
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
odłączanie banku, pierwsze wdrożenie serwerowe i pomiar jakości OCR. To nie jest obietnica
ich realizacji ani lista automatycznych blockerów wszystkich zmian.

Rozszerzenie walut, podziału produktów i reguł nowych źródeł finansowych wymaga
osobnego ustalenia. Obecne zaokrąglanie udziałów i progi alertów mają już kontrakty;
nie oznaczaj ich ponownie jako całkowicie nierozstrzygniętych. Automatyczne
powiązanie paragon/bank i push pozostają rozszerzeniami, nie brakami bieżącego inboxa.

Przed wdrożeniem wieloinstancyjnym uwzględnij lokalny magazyn Google challenge,
lokalne limity IP i brak trwałego outboxa/cleanup workera. Nie deklaruj gotowości
produkcyjnej na podstawie samych testów lokalnych. Rzeczywiste Google, SMTP,
Tink, OpenRouter/OCR i S3 wymagają odrębnej kontroli z bezpiecznymi danymi.

## Granice potwierdzenia

Konfiguracja kodowa obejmuje GHCR, migracje i kolejność CD, ale nie potwierdza
istnienia VM, ustawień GitHuba ani działania DNS/HTTPS i firewalla. Uzgodniony
wariant to 4money.com.pl osiągalne przez Tailscale; JSON nie konfiguruje sieci.
Przed pierwszym uruchomieniem trzeba sprawdzić logowanie, zapis wydatku,
upload/podpisany odczyt plików i OCR na rzeczywistym środowisku.

Testy PostgreSQL wymagają disposable bazy, S3 jawnego włączenia, a atrapy nie
potwierdzają usług zewnętrznych. Polecenia: [development](development.md#kontrole-backendu).
