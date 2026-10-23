# Architektura systemu

Czytaj przed zmianami backendu, zależności modułów, persystencji i integracji.
To opis architektury projektowanej wraz z potwierdzonymi elementami kodu;
stopień realizacji opisuje [status](implementation-status.md).

## Modularny monolit

Backend jest jedną solucją .NET z hostem `ZPI4M.Api`, projektami modułów,
`ZPI4M.Shared` oraz `ZPI4M.Database`. Sam podział na projekty nie oznacza
osobnych usług ani niezależnych wdrożeń.

| Moduł | Odpowiedzialność | Projektowane zależności |
| --- | --- | --- |
| Banking | Integracja bankowa i pobieranie transakcji | Tylko OpenBanking |
| Receipt | Przetwarzanie obrazów paragonów | Tylko model OCR |
| Expense | Ewidencja wydatków i kategoryzacja | Banking, Receipt, model kategoryzujący, User, Grouping |
| Limits | Budżety i progi wydatkowe | Expense, Grouping, Notifications, User |
| Grouping | Grupy, podział kosztów i zobowiązania | Notifications, User |
| Reports | Raporty i statystyki | Expense, Limits, Grouping, User |
| Notifications | Powiadomienia i alerty | User |
| User | Tożsamość i uprawnienia | Brak innych modułów |

Tabela opisuje projekt zależności z `../doc/Architektura systemu.txt`
i diagramu systemu; tekst ponownie sprawdzono 2026-10-23, diagram 2026-10-21.
Nie jest to lista aktualnych
`ProjectReference`. Przed dodaniem
zależności sprawdź kod i istniejące kontrakty.
Nie wprowadzaj mikroserwisów ani nowego wzorca komunikacji bez potrzeby zadania.

Zgodnie z aktualnym opisem i diagramem Expense koordynuje dodawanie wydatku: przez
publiczny interfejs Receipt zleca odczyt paragonu, a przez interfejs Banking
pobiera transakcje do wyboru lub dopasowania. Expense odpowiada za utworzenie
wydatku i zapisanie powiązań. Receipt korzysta wyłącznie z modelu OCR,
a Banking wyłącznie z modułu/integracji OpenBanking. Receipt i Banking nie
zależą od Expense ani od siebie nawzajem. Są to ustalenia dotyczące współpracy
modułów i integracji; nie oznaczają, że projekty OCR lub OpenBanking już istnieją
w solucji.

## Warstwy modułu

| Warstwa | Rola i kierunek użycia |
| --- | --- |
| ApiControllers | HTTP, mapowanie odpowiedzi; korzysta z Services i DTOs |
| Services | Logika biznesowa i transformacje; korzysta z Repositories, Models i DTOs |
| Repositories | Dostęp do bazy i izolacja zapytań; korzysta z kontekstu i Models |
| Database | Kontekst persystencji i konfiguracja mapowania modeli |
| Models | Model danych; nie jest automatycznie kontraktem HTTP |
| DTOs | Dane przekazywane przez granicę API |
| Integrations, opcjonalnie | Adaptery usług zewnętrznych, np. OCR lub bankowości |

Wszystkie błędy HTTP korzystają ze standardowego formatu `ProblemDetails`,
bez dostosowywania pól. Kontrolery zwracają błędy biznesowe przez `Problem()`.
Walidacja DTO zwraca `ValidationProblemDetails` z błędami poszczególnych pól.
Host zapewnia `ProblemDetails` dla pustych błędów HTTP i nieobsłużonych wyjątków.

Schemat przepływu to `ApiControllers → Services → Repositories → baza`.
Zaimplementowane repozytoria mają osobne katalogi
`Repositories/<Nazwa>Repository/` z interfejsem, implementacją i opcjonalnym
plikiem `<Nazwa>Queries.cs`. Namespace odzwierciedla katalog konkretnego
repozytorium: `ZPI4M.<Moduł>.Repositories.<Nazwa>Repository`, np.
`ZPI4M.Grouping.Repositories.GroupMemberRepository`. Ta sama przestrzeń nazw
obejmuje interfejs, implementację i Queries. UserToken i Notification nie mają Queries.
Repozytoria rejestrują dodanie i usunięcie encji, ale nie wykonują własnego
`SaveChangesAsync`. Granicę zapisu wyznacza serwis, aby np. utworzenie grupy
i dodanie administratora zapisać atomowo. Wspólny zapis wymaga tego samego
scoped kontekstu; osobne konteksty modułów nie współdzielą automatycznie
transakcji ani change trackera.
Metody `UserTokenRepository.TryConsumeAsync` i `UserRepository.TryConfirmEmailAsync`
wykonują warunkowe DELETE/UPDATE natychmiast. Serwis potwierdzenia e-maila
obejmuje oba wywołania jedną transakcją na wspólnym `UserDbContext`, aby
zapewnić jednorazowe zużycie tokenu i atomowe potwierdzenie adresu.
Potwierdzanie i ponowna wysyłka używają `UserRepository.GetByIdForUpdateAsync`
w transakcji: parametryzowane zapytanie PostgreSQL `SELECT ... FOR UPDATE`
blokuje rekord użytkownika przed operacjami na tokenie. Dzięki wspólnej
kolejności blokad żądania potwierdzenia i ponownej wysyłki są serializowane
dla danego konta. Wywołania SMTP odbywają się po zakończeniu zapisu/transakcji.
Serwisy User wysyłają wiadomości bezpośrednio przez wspólny IEmailSender.
W Integrations/Smtp znajdują się adapter SmtpEmailSender, konfiguracja,
UserEmailMessages.cs z tematami oraz treściami i rekordem EmailMessage
(Subject, Body) w tym samym pliku. Nie ma folderu Services/Email ani osobnych
senderów dla poszczególnych rodzajów wiadomości.
Obsługa błędu wysyłki pozostaje w serwisie danego przepływu, ponieważ reset hasła
zachowuje inną odpowiedź niż pozostałe operacje.
Potwierdzenie dezaktywacji używa tej samej kolejności blokad i transakcji:
jednorazowo zużywa token, ustawia IsActive = false i usuwa wszystkie tokeny konta.
Każda walidacja JWT sprawdza istnienie sesji i aktywność konta w bazie,
więc dezaktywacja odrzuca też wcześniej wydane JWT przy kolejnych żądaniach.
Kontrakty Services i DTOs mogą istnieć przed implementacją pełnego przepływu.
Wydzielanie Integrations jest możliwością projektową, nie wymogiem tworzenia
pustych katalogów w każdym module.

## Persystencja i migracje

PostgreSQL przechowuje dane w schematach modułowych. Moduły mają własne
konteksty, a konfiguracje `*Configuration.Configure` są wykorzystywane także
przez wspólny `AppDbContext` w projekcie `ZPI4M.Database`.
Migracje całego modelu znajdują się w tym projekcie; tabela historii migracji
jest skonfigurowana w schemacie `public`.

Nazwy tabel i kolumn są mapowane jawnie, zwykle w snake_case. Przy zmianie
modelu sprawdź konfigurację, kontekst modułu i wspólny kontekst migracji.
Nie zakładaj, że sama zmiana właściwości C# aktualizuje istniejącą bazę.

SQL w `database/init` tworzy bazową strukturę przy pierwszej inicjalizacji
kontenera. Historia migracji rozwija ten stan. Procedura i ograniczenia
znajdują się w [instrukcji pracy](development.md#modele-i-migracje).

## Stos obecny i docelowy

| Obszar | Stan potwierdzony / założenie |
| --- | --- |
| Backend | C#, .NET 10, ASP.NET Core, EF Core, Npgsql |
| API | Obecnie OpenAPI w Development; Swagger i Postman wymieniono w planie narzędzi |
| Baza lokalna | PostgreSQL 15 Alpine w Docker Compose |
| Web | Docelowo React; brak aplikacji frontendowej w obecnym repo |
| Mobile | Plan React + Capacitor lub React Native; wybór nie jest rozstrzygnięty |
| OCR | Plan Python i integracja OCR; brak implementacji pipeline'u w obecnym repo |
| Automatyzacja | GitHub Actions, pre-commit, narzędzia Python zarządzane przez uv |
| Testy backendu | Brak projektów w bieżącej solucji; tests zawiera tylko .gitkeep, prace nad testami User odłożono na osobną gałąź |
| Infrastruktura | Obecna konfiguracja Terraform dla Proxmox; Linux i Tailscale w środowisku developerskim |
| Prezentacja | AWS jako założenie docelowe, nie potwierdzone wdrożenie aplikacji |

Modele Banking zawierają identyfikatory Tink, ale w obecnej gałęzi nie ma
jeszcze kontrolera, serwisów ani wykonanej integracji dostawcy.

## Źródła

- [Opis architektury](../../doc/Architektura%20systemu.txt)
- [Diagram systemu](../../doc/Architektura%20systemu.png) i [warstw modułu](../../doc/Architektura%20modułu.png)
- [Plan stosu technologicznego](../../doc/Stos%20technologiczny.txt)
- [Host API](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs)
- [Wspólny kontekst migracji](../backend/ZPI4M.Backend/src/ZPI4M.Database/AppDbContext.cs)
- [Compose](../containers/docker-compose.yml)
- [Infrastruktura Proxmox i jej proces wdrożenia](../proxmox-infra/README.md)
