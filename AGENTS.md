# Kontekst dla agentów: 4Money

4Money (For Money, 4M) wspiera zarządzanie finansami osobistymi i współdzielonymi.
Łączy wydatki ręczne, paragony i dane bankowe w jeden model. Wyróżnikiem jest
analiza poszczególnych produktów, a także podział kosztów między użytkowników.

## Jak rozpocząć sesję

1. Ustal zakres zadania i sprawdź aktualny stan plików, których dotyczy.
2. Dobierz lekturę z tabeli poniżej. Dokumenty tematyczne są kontekstem projektu;
   nie trzeba wczytywać całego zestawu przy każdym zadaniu.
3. Przed zmianą kontraktu lub zachowania sprawdź wymagania oraz istniejącą implementację.
4. Dobierz weryfikację do zmiany i podaj, co rzeczywiście zostało sprawdzone.

| Rodzaj zadania | Dokumenty do przeczytania |
| --- | --- |
| Wymagania, MVP, UI web/mobile | [Produkt](agent-context/product.md), [domena](agent-context/domain.md) |
| Backend i kontrakty API | [Domena](agent-context/domain.md), [architektura](agent-context/architecture.md), [praca w repo](agent-context/development.md), właściwy obszar [statusu](agent-context/implementation-status.md) |
| Banking i import transakcji | Dokumenty backendowe powyżej oraz [kontrakt Banking](backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md) |
| Notifications i inbox | Dokumenty backendowe powyżej oraz [kolekcje Postman](postman/README.md) i [testy backendu](backend/ZPI4M.Backend/tests/README.md) |
| OCR i kategoryzacja | [Produkt](agent-context/product.md), [domena](agent-context/domain.md), [architektura](agent-context/architecture.md), [status](agent-context/implementation-status.md) |
| Modele i migracje | [Domena](agent-context/domain.md), [architektura](agent-context/architecture.md), [praca w repo](agent-context/development.md), [status](agent-context/implementation-status.md) |
| Testy, hooki i CI backendu | [Praca w repo](agent-context/development.md), [status](agent-context/implementation-status.md), [narzędzia](tools/README.md) |
| Infrastruktura | [Architektura](agent-context/architecture.md), [instrukcja Proxmox](proxmox-infra/README.md) i workflow dotyczący zadania |

## Najważniejsze granice produktu

- Wydatek, pozycja wydatku, paragon i transakcja bankowa to różne pojęcia.
- Użytkownik może poprawić OCR i kategorię oraz zaakceptować lub odrzucić
  proponowane powiązanie paragonu z transakcją bankową.
- System prezentuje udziały i zobowiązania; rozliczenie finansowe odbywa się poza aplikacją.
- Projekt nie obejmuje wykonywania przelewów, przechowywania środków,
  obsługi kart płatniczych, zastępowania aplikacji bankowej ani doradztwa finansowego.
- MVP dopuszcza dostawcę Open Banking, sandbox lub symulator. Obecność kontraktu
  API nie oznacza działającej integracji.
- MVP powiadomień obejmuje inbox web/mobile i indywidualny stan odczytu.
  Push jest rozszerzeniem, poza bieżącym zakresem; nie dodawaj infrastruktury push.

## Orientacja w repozytorium

| Miejsce | Zawartość |
| --- | --- |
| `backend/ZPI4M.Backend/` | Solucja .NET, moduły, host API, migracje i testy |
| `agent-context/` | Kontekst tematyczny i bieżący status implementacji |
| `../doc/` (lokalne `ZPI/doc`) | Źródłowe wymagania TXT i diagramy poza repozytorium |
| `containers/` | Compose lokalnej bazy PostgreSQL |
| `database/init/` | SQL inicjalizujący bazę przed zastosowaniem migracji |
| `database/seed_local.sql` | Reset i syntetyczne dane lokalne; uruchamiaj po migracjach |
| `postman/` | Kolekcje User, Banking, Notifications i diagnostyka Tink; wspólne szablony środowisk |
| `tools/` | Narzędzia Python/uv, hooki i pomocnicze kontrole backendu |
| `.github/workflows/` | CI backendu oraz planowanie i wdrażanie infrastruktury |
| `notes/` | Szczegółowe instrukcje migracji i hooków |
| `proxmox-infra/` | Terraform dla środowiska Proxmox |

## Zasady pracy

- Bieżące ID użytkownika pobieraj przez `ZPI4M.Shared.CurrentUser.ICurrentUser`.
  Wspólna implementacja `HttpCurrentUser` znajduje się w tym samym folderze;
  host rejestruje ją jako scoped wraz z IHttpContextAccessor. Nie twórz adapterów
  dla poszczególnych modułów ani nie parsuj ponownie claimu `sub` w kontrolerach.
  Brak poprawnego ID obsługuj jako 401; ten kontrakt nie zastępuje walidacji JWT.
- Zachowuj modularny monolit i istniejące granice warstw. Szczegóły opisuje
  [architektura](agent-context/architecture.md).
- Kod i konfiguracja potwierdzają aktualne zachowanie; dokumentacja produktowa
  opisuje wymagania. Wykrytą rozbieżność nazwij, zamiast bez uzasadnienia
  dopasowywać wymagania do kodu lub zmieniać kod do niejasnego zapisu.
- Modele i katalogi nie dowodzą ukończenia funkcji. Sprawdzaj pełny przepływ
  i ograniczenia opisane w [statusie implementacji](agent-context/implementation-status.md).
- Nie dodawaj sekretów, rzeczywistych danych finansowych ani materiałów
  autoryzacyjnych do kodu, logów lub dokumentacji. Używaj danych syntetycznych.
- Przed zmianami uwzględnij istniejące zmiany użytkownika; nie nadpisuj ich
  i nie dołączaj do zadania przypadkowych plików.
- Sprawdzenie formatowania nie zastępuje kompilacji ani testów. CI może pominąć
  projekt testowy, którego nazwa nie pasuje do filtrów.
- Uruchamiaj operacje na bazie i infrastrukturze tylko w zakresie zadania;
  zwykła kontrola dokumentacji nie wymaga migracji ani wdrożenia.
- Zachowuj konwencje z `backend/.editorconfig` i istniejących hooków.
  Konwencje gałęzi i commitów ClickUp opisuje [instrukcja narzędzi](tools/README.md#clickup-branch-and-commit-conventions).

## Orientacja w implementacji — 2026-10-28

- Notifications ma wewnętrzne `NotifyAsync`, autoryzowane pobieranie inboxa,
  odczyt pojedynczy i zbiorczy. Operacje odbiorców należą do osobnego
  `NotificationRecipientRepository`. Wywołania z Grouping/Limits nie są jeszcze podłączone.
- NotifyAsync zwraca Guid?: tworzy odbiorców tylko dla aktywnych kont zwróconych
  przez IUserLookupService jednym zapytaniem po ID. Nieaktywne i nieistniejące
  konta są pomijane; pusta lista lub brak aktywnych odbiorców daje null bez zapisu.
- ExpenseDbContext, GroupingDbContext i NotificationsDbContext współdzielą scoped
  NpgsqlConnection. To przygotowanie do integracji, nie automatyczna transakcja:
  koordynator musi dołączyć konteksty i wykonać końcowy commit. Operacje wykonuj
  kolejno. Pozostałe konteksty mają osobne połączenia.
  Przy implementacji zapisu Expense/Grouping z powiadomieniem zastosuj
  [checklistę transakcji](agent-context/architecture.md#do-wykonania-przy-implementacji-expense-i-grouping)
  i dodaj testy rollbacku całej operacji; wywołany moduł nie zatwierdza transakcji koordynatora.
- Pary interfejs/implementacja User znajdują się w Services/Lookup oraz
  Services/BankingProfile, a Notifications w Services/Notifications.
- Notifications importuje User, nie Expense. FK do Expense konfiguruje wspólny
  `AppDbContext`; samo powiązanie tabel nie wymaga zależności projektów modułów.
- Banking ma integrację Tink, odczyty i worker importu, a User obsługuje tożsamość,
  sesje oraz operacje konta. Aktualne ograniczenia są w statusie; nie cofaj ich
  opisu do samych modeli na podstawie starszych fragmentów sprzed rebase.
- Solucja ma dziewięć projektów xUnit: po trzy User, Banking i Notifications.
  Starsze wyniki testów dotyczą konkretnych rewizji, nie dowodzą poprawności po rebase.
- Wszystkie testy .NET z PostgreSQL używają TEST_POSTGRES_CONNECTION_STRING;
  pipeline ustawia ją raz. Brak konfiguracji lokalnie pomija testy bazy, a w CI
  jest błędem. Ostatnie wyniki częściowych przebiegów opisuje status implementacji.
- Aktualna migracja inboxa to `20261027172612_AddNotificationsAfterRebase`,
  po `20261026215244_AddBanking`. Nie przywracaj usuniętej migracji
  `20261026233657_AddNotificationRecipient` obok jej zamiennika.
- Nowa baza wymaga kolejności: SQL init → migracje EF → seed. Seed czyści dane,
  nie naprawia schematu ani historii EF. Rebase nie cofa już zastosowanych zmian bazy;
  odtworzenie lokalnego wolumenu wymaga zgody na usunięcie danych w danym zadaniu.
- Dla uruchomienia lokalnego używaj właściwych plików `.env`: Compose w `containers/`,
  API w katalogu `ZPI4M.Api`. Domyślny profil API to HTTP na 5283;
  Postman `baseUrl` musi odpowiadać rzeczywiście uruchomionemu profilowi.
- Szablon ZPI4M.Api/.env.example zawiera także SMTP i nazwy zmiennych wielkimi
  literami. Sekrety uzupełniaj wyłącznie lokalnie; nie kopiuj .env do dokumentacji.

## Utrzymanie kontekstu

Opis produktu ma główne miejsce w `product.md`, pojęcia w `domain.md`, organizacja
systemu w `architecture.md`, polecenia w `development.md`, a bieżące braki
i rozbieżności w `implementation-status.md`. W innych miejscach odsyłaj do nich.

Po zmianie architektury, konfiguracji, kontraktu lub sposobu pracy aktualizuj
właściwy dokument. Po zmianie istotnego stanu implementacji aktualizuj jego opis
i datę weryfikacji. Nie dopisuj wyników testów, których nie wykonano.

Materiały źródłowe w lokalnym `../doc/` pozostają dostępne przez odnośniki w dokumentach
tematycznych. Otwarte decyzje produktowe wymagają ustalenia w ramach zadania;
ten zestaw nie rozstrzyga ich przez domysł.
