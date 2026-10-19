# Stan implementacji i kwestie otwarte

Zweryfikowano: **2026-10-19**, na podstawie lokalnego `ZPI/doc` i kodu repozytorium.
To przegląd statyczny, nie potwierdzenie udanego builda, testów ani wdrożenia.
Czytaj przed pracą nad danym obszarem; po zmianach sprawdź ponownie źródła.

## Potwierdzony stan

| Obszar | Co istnieje | Ograniczenie |
| --- | --- | --- |
| Backend | Solucja .NET 10, host API, projekty ośmiu modułów i Shared | Struktura projektu nie potwierdza realizacji MVP |
| Baza | Modele i konfiguracje Banking, Expense, Grouping, Limits, Notifications, Receipt i User; wspólny AppDbContext i migracje | Obecność migracji nie potwierdza ich zastosowania na każdej bazie |
| Repozytoria | Banking, Grouping, User, UserToken, ExpenseShare i Notification | Nie wykonują SaveChangesAsync; nie stanowią kompletnego API wszystkich encji |
| User | User, UserToken, konfiguracja EF, repozytoria, serwis rejestracji i POST /api/users/register z walidacją DTO i hashowaniem hasła | Rejestracja tworzy niepotwierdzone konto; brak logowania, weryfikacji e-maila, resetu hasła i integracji SMTP |
| Banking | Modele rachunków i transakcji, konfiguracja i repozytoria | Brak kontrolera, serwisów i integracji dostawcy w obecnej gałęzi |
| Receipt / OCR | Model paragonu i konfiguracja EF | Brak pipeline'u OCR, uploadu oraz potwierdzonej integracji magazynu obrazów |
| Kategoryzacja | Model kategorii | Brak implementacji modelu klasyfikującego i wyników ewaluacji |
| Limits | Model limitu kategorii i pól limitów grupy | Brak potwierdzonego przepływu alertów i kontroli budżetów |
| Notifications | Notification, NotificationsDbContext, NotificationsConfiguration, repozytorium Add/GetByIdAsync i DI; konfiguracja uwzględniona w AppDbContext | Brak serwisu wysyłki; nie planujemy historii, paginacji, Update ani Remove |
| Reports | Projekt modułu | Brak usług raportowania |
| Web / mobile | Założenia w dokumentacji | Brak projektów aplikacji klienckich w repozytorium |
| Narzędzia | Hooki, testy hooków, workflow backendu | Brak projektów testowych backendu; komunikat CI „not configured” nie oznacza wykonania testów |
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
| SQL init ma stare pola refresh tokenu, nie zawiera UserToken i używa timestampów bez strefy | Migracje AddUserTokenTable i TimestampTypeChanges rozwijają stan bazowy; nie aktualizuj historycznego SQL init przy zwykłej zmianie modelu |
| Notifications istniało w SQL init, ale brakowało go w początkowym modelu EF | AddNotificationsConfigurationClass uzupełnia snapshot i zmienia typ sent_at, zamiast ponownie tworzyć tabelę |
| Opis bazy używa `ExpenceShare` | Aktualna nazwa klasy to `ExpenseShare`; nie kopiuj literówki do nowych kontraktów |
| Produkt obejmuje finanse osobiste, ale Expense wymaga grupy | Sposób reprezentowania osobistej przestrzeni wymaga ustalenia |
| Dokumentacja stosu wymienia Swagger, mobile i Python OCR | Nie oznacza obecności UI Swagger ani tych aplikacji w kodzie |

Źródła: [pomocnik CI](../tools/ci/backend_checks.sh),
[SQL bazowy](../database-init/02-tables.sql),
[migracje](../backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/),
[konfiguracja Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Database/ExpenseConfiguration.cs),
[model Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/Models/Expense.cs),
[opis bazy](../../doc/Baza%20danych.txt), [stos](../../doc/Stos%20technologiczny.txt).

## Otwarte ustalenia

- Wariant mobile: Capacitor czy React Native.
- Docelowa implementacja uwierzytelniania i kontrakt tożsamości modułu User.
- Konfiguracja skrzynki SMTP; zewnętrzny dostawca jest uzgodniony, działająca wysyłka nie jest jeszcze zaimplementowana.
- Dostawca lub symulator importu bankowego i szczegóły integracji.
- Usługa OCR, magazyn obrazów, kontrakt kategoryzacji i sposób pomiaru jakości.
- Reguły podziału kosztów, sald i limitów, wskazane w [domenie](domain.md#kwestie-wymagające-ustalenia-przy-zadaniu).

Nie są to automatyczne blokery wszystkich prac. Ustalaj tylko decyzje potrzebne
do konkretnego zadania. Po realizacji aktualizuj odpowiedni wiersz statusu,
źródła i datę weryfikacji; nie używaj tego pliku jako dziennika każdej sesji.
