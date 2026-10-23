# Stan implementacji i kwestie otwarte

Zweryfikowano: **2026-10-23**, na podstawie aktualnego kodu, solucji, migracji,
workflow i tekstów w lokalnym `ZPI/doc` (`../doc` względem katalogu głównego
repozytorium). Wcześniejszy przegląd diagramów zachowuje datę 2026-10-21.
To przegląd statyczny, nie potwierdzenie zastosowania migracji, testów ani wdrożenia.
Czytaj przed pracą nad danym obszarem; po zmianach sprawdź ponownie źródła.

## Potwierdzony stan

| Obszar | Co istnieje | Ograniczenie |
| --- | --- | --- |
| Backend | Solucja .NET 10, host API, projekty ośmiu modułów i Shared | Struktura projektu nie potwierdza realizacji MVP |
| Baza | Modele i konfiguracje Banking, Expense, Grouping, Limits, Notifications, Receipt i User; wspólny AppDbContext i migracje | Obecność migracji nie potwierdza ich zastosowania na każdej bazie |
| Expense | Modele wydatku, pozycji, kategorii i udziałów; repozytorium udziałów; osobne unikalne indeksy FK paragonu i transakcji w konfiguracji, snapshocie i migracji AddUniqueExpenseSourceReferences | Brak kontrolera i serwisów tworzenia, edycji oraz dopasowania wydatków |
| Repozytoria | Banking, Grouping, User, UserToken, ExpenseShare i Notification | Nie wykonują SaveChangesAsync; warunkowe UPDATE/DELETE dla potwierdzania e-maila wykonują się od razu; brak kompletnego API wszystkich encji |
| User | Rejestracja, potwierdzenie i ponowna wysyłka e-maila; login z JWT i hashowanym refresh tokenem, rotacja refresh, logout; jedna sesja na konto i weryfikacja jej aktualności przy uwierzytelnieniu; limity IP; GET/PUT api/users/profile; zmiana e-maila z aktualnym hasłem i potwierdzeniem tokenem; forgot-password/reset-password z unieważnieniem sesji; dezaktywacja po potwierdzeniu e-mailem z usunięciem wszystkich tokenów; login nieaktywnego konta po poprawnym haśle zwraca 403; wspólny IEmailSender/SmtpEmailSender i UserEmailMessages | Brak strony potwierdzenia/resetu, zmiany hasła po podaniu aktualnego hasła; konfiguracja JWT wymaga klucza w środowisku; w bieżącym stanie brak projektów testów backendu i potwierdzenia pełnego przepływu na PostgreSQL oraz rzeczywistej skrzynce |
| Banking | Modele rachunków i transakcji, konfiguracja i repozytoria | Brak kontrolera, serwisów i integracji dostawcy w obecnej gałęzi |
| Receipt / OCR | Model paragonu i konfiguracja EF | Brak pipeline'u OCR, uploadu oraz potwierdzonej integracji magazynu obrazów |
| Kategoryzacja | Model kategorii | Brak implementacji modelu klasyfikującego i wyników ewaluacji |
| Limits | Model limitu kategorii i pól limitów grupy | Brak potwierdzonego przepływu alertów i kontroli budżetów |
| Notifications | Notification, NotificationsDbContext, NotificationsConfiguration, repozytorium Add/GetByIdAsync i DI; konfiguracja uwzględniona w AppDbContext | Brak serwisu wysyłki; nie planujemy historii, paginacji, Update ani Remove |
| Reports | Projekt modułu | Brak usług raportowania |
| Web / mobile | Założenia w dokumentacji | Brak projektów aplikacji klienckich w repozytorium |
| Narzędzia | Hooki, testy hooków, workflow backendu z wykrywaniem zestawów unit/integration | W tests pozostał tylko .gitkeep, solucja nie zawiera projektów testowych backendu; prace nad testami User odłożono na osobną gałąź; „not configured” nie oznacza wykonania testów |
| Infrastruktura | Compose PostgreSQL i Terraform Proxmox z workflow | Brak potwierdzenia uruchomienia infrastruktury i wdrożenia 4M; AWS pozostaje planem |

Źródła: [solucja](../backend/ZPI4M.Backend/ZPI4M.Backend.slnx),
[moduły](../backend/ZPI4M.Backend/src/modules/),
[host](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs),
[AppDbContext](../backend/ZPI4M.Backend/src/ZPI4M.Database/AppDbContext.cs),
[infrastruktura](../proxmox-infra/README.md).


## Rozbieżności istotne podczas pracy

| Rozbieżność | Znaczenie dla agenta |
| --- | --- |
| CI wybiera `.UnitTests.csproj` i `.IntegrationTests.csproj`; obecnie nie ma projektów testowych backendu | Nie traktuj „not configured” jako udanych testów |
| SQL inicjalizacyjny tworzy `grouping.expense_share`; obecny model mapuje `expense.expense_share` | Zastosuj istniejącą migrację przenoszącą tabelę; nie traktuj SQL jako końcowego stanu modelu |
| Pierwsza migracja ma puste Up/Down | Zakłada bazę zainicjalizowaną SQL-em; sam `database update` nie tworzy pełnego schematu od zera |
| Opis bazy w doc wymienia tokeny 1–4; kod dodaje AccountDeactivation = 5 | Migracja 20261023131244_AddAccountDeactivationTokens rozszerza oba CHECK tokenów; przed użyciem dezaktywacji zastosuj ją na właściwej bazie. Down usuwa tokeny rodzaju 5 przed przywróceniem ograniczeń |
| SQL init ma stare pola refresh tokenu, nie zawiera UserToken i używa timestampów bez strefy | Migracje AddUserTokenTable i TimestampTypeChanges rozwijają stan bazowy; nie aktualizuj historycznego SQL init przy zwykłej zmianie modelu |
| Notifications istniało w SQL init, ale brakowało go w początkowym modelu EF | AddNotificationsConfigurationClass uzupełnia snapshot i zmienia typ sent_at, zamiast ponownie tworzyć tabelę |
| Opis bazy używa `ExpenceShare` | Aktualna nazwa klasy to `ExpenseShare`; nie kopiuj literówki do nowych kontraktów |
| SQL init nie ma UNIQUE dla FK paragonu i transakcji w Expense; aktualny opis bazy, EF i snapshot już je uwzględniają | Migracja 20261021162958_AddUniqueExpenseSourceReferences tworzy dwa osobne unikalne indeksy; po jej zastosowaniu jeden paragon lub transakcja mogą należeć do najwyżej jednego wydatku |
| Nie wszystkie FK między modułami i domyślne ograniczenia z SQL init są odwzorowane w EF | Przy zmianie schematu porównuj SQL, migracje i snapshot; szczegóły opisuje domain.md |
| Produkt obejmuje finanse osobiste, ale Expense wymaga grupy | Sposób reprezentowania osobistej przestrzeni wymaga ustalenia |
| ExpenseItem ma opcjonalny FkUserId, ale Funkcjonalności.txt (sekcja 3) umieszczają przypisywanie uczestników do produktów poza MVP | MVP obejmuje podział całego wydatku; obecność pola w modelu nie oznacza gotowej funkcji ani obowiązku realizacji rozszerzenia |
| Dokumentacja stosu wymienia Swagger, mobile i Python OCR | Nie oznacza obecności UI Swagger ani tych aplikacji w kodzie |

Źródła: [pomocnik CI](../tools/ci/backend_checks.sh),
[SQL bazowy](../database/init/02-tables.sql),
[migracje](../backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/),
[konfiguracja Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Database/ExpenseConfiguration.cs),
[model Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Models/Expense.cs),
[opis bazy](../../doc/Baza%20danych.txt), [stos](../../doc/Stos%20technologiczny.txt).

Aktualny diagram logiczny i opis bazy używają `birth_date`, zgodnie z mapowaniem
`User.BirthDate`. Dawna rozbieżność `birthday` nie występuje już w tych źródłach.

## Otwarte ustalenia

- Wariant mobile: Capacitor czy React Native.
- Dalsze operacje konta i uprawnienia między modułami. Uwierzytelnianie używa JWT (`sub` = ID użytkownika, `sid` = ID sesji) i rotowanego refresh tokenu; szczegóły w development.md.
- Testy modułu User będą realizowane na osobnej gałęzi. W bieżącym kodzie nie ma ich projektów, obsługi środowiska Testing ani testowego PostgreSQL w workflow; nie przypisuj tej gałęzi wcześniejszych wyników usuniętego zestawu.
- Konfiguracja skrzynki SMTP; adapter jest zaimplementowany, wybór serwera i faktyczne dostarczenie wiadomości nie są jeszcze potwierdzone.
- Dostawca lub symulator importu bankowego i szczegóły integracji.
- Usługa OCR, magazyn obrazów, kontrakt kategoryzacji i sposób pomiaru jakości.
- Reguły podziału kosztów, sald i limitów, wskazane w [domenie](domain.md#kwestie-wymagające-ustalenia-przy-zadaniu).

Nie są to automatyczne blokery wszystkich prac. Ustalaj tylko decyzje potrzebne
do konkretnego zadania. Po realizacji aktualizuj odpowiedni wiersz statusu,
źródła i datę weryfikacji; nie używaj tego pliku jako dziennika każdej sesji.
