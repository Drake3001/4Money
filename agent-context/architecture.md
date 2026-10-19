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
| Banking | Integracja bankowa i pobieranie transakcji | Open Banking, Expense |
| Receipt | Przetwarzanie obrazów paragonów | OCR, Expense |
| Expense | Ewidencja wydatków i kategoryzacja | Model kategoryzujący, User, Grouping |
| Limits | Budżety i progi wydatkowe | Expense, Grouping, Notifications, User |
| Grouping | Grupy, podział kosztów i zobowiązania | Notifications, User |
| Reports | Raporty i statystyki | Expense, Limits, Grouping, User |
| Notifications | Powiadomienia i alerty | User |
| User | Tożsamość i uprawnienia | Brak innych modułów |

Tabela odtwarza projekt zależności z `doc`; nie jest listą aktualnych
`ProjectReference`. Przed dodaniem zależności sprawdź kod i istniejące kontrakty.
Nie wprowadzaj mikroserwisów ani nowego wzorca komunikacji bez potrzeby zadania.

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

Schemat przepływu to `ApiControllers → Services → Repositories → baza`.
Repozytoria rejestrują dodanie i usunięcie encji, ale nie wykonują własnego
`SaveChangesAsync`. Granicę zapisu wyznacza serwis, aby np. utworzenie grupy
i dodanie administratora zapisać atomowo. Wspólny zapis wymaga tego samego
scoped kontekstu; osobne konteksty modułów nie współdzielą automatycznie
transakcji ani change trackera.
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

SQL w `database-init` tworzy bazową strukturę przy pierwszej inicjalizacji
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
