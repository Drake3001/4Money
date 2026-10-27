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
| Wymagania, MVP, UI web/mobile | [Produkt](doc/agent-context/product.md), [domena](doc/agent-context/domain.md) |
| Backend i kontrakty API | [Domena](doc/agent-context/domain.md), [architektura](doc/agent-context/architecture.md), [praca w repo](doc/agent-context/development.md), właściwy obszar [statusu](doc/agent-context/implementation-status.md) |
| Banking i import transakcji | Dokumenty backendowe powyżej oraz [kontrakt Banking](backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md) |
| OCR i kategoryzacja | [Produkt](doc/agent-context/product.md), [domena](doc/agent-context/domain.md), [architektura](doc/agent-context/architecture.md), [status](doc/agent-context/implementation-status.md) |
| Modele i migracje | [Domena](doc/agent-context/domain.md), [architektura](doc/agent-context/architecture.md), [praca w repo](doc/agent-context/development.md), [status](doc/agent-context/implementation-status.md) |
| Testy, hooki i CI backendu | [Praca w repo](doc/agent-context/development.md), [status](doc/agent-context/implementation-status.md), [narzędzia](tools/README.md) |
| Infrastruktura | [Architektura](doc/agent-context/architecture.md), [instrukcja Proxmox](proxmox-infra/README.md) i workflow dotyczący zadania |

## Najważniejsze granice produktu

- Wydatek, pozycja wydatku, paragon i transakcja bankowa to różne pojęcia.
- Użytkownik może poprawić OCR i kategorię oraz zaakceptować lub odrzucić
  proponowane powiązanie paragonu z transakcją bankową.
- System prezentuje udziały i zobowiązania; rozliczenie finansowe odbywa się poza aplikacją.
- Projekt nie obejmuje wykonywania przelewów, przechowywania środków,
  obsługi kart płatniczych, zastępowania aplikacji bankowej ani doradztwa finansowego.
- MVP dopuszcza dostawcę Open Banking, sandbox lub symulator. Obecność kontraktu
  API nie oznacza działającej integracji.

## Orientacja w repozytorium

| Miejsce | Zawartość |
| --- | --- |
| `backend/ZPI4M.Backend/` | Solucja .NET, moduły, host API, migracje i testy |
| `doc/` | Materiały projektowe, diagramy i niniejszy kontekst tematyczny |
| `containers/` | Compose lokalnej bazy PostgreSQL |
| `database-init/` | SQL inicjalizujący bazę przed zastosowaniem migracji |
| `tools/` | Narzędzia Python/uv, hooki i pomocnicze kontrole backendu |
| `.github/workflows/` | CI backendu oraz planowanie i wdrażanie infrastruktury |
| `notes/` | Szczegółowe instrukcje migracji i hooków |
| `proxmox-infra/` | Terraform dla środowiska Proxmox |

## Zasady pracy

- Zachowuj modularny monolit i istniejące granice warstw. Szczegóły opisuje
  [architektura](doc/agent-context/architecture.md).
- Kod i konfiguracja potwierdzają aktualne zachowanie; dokumentacja produktowa
  opisuje wymagania. Wykrytą rozbieżność nazwij, zamiast bez uzasadnienia
  dopasowywać wymagania do kodu lub zmieniać kod do niejasnego zapisu.
- Modele i katalogi nie dowodzą ukończenia funkcji. Sprawdzaj pełny przepływ
  i ograniczenia opisane w [statusie implementacji](doc/agent-context/implementation-status.md).
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

## Utrzymanie kontekstu

Opis produktu ma główne miejsce w `product.md`, pojęcia w `domain.md`, organizacja
systemu w `architecture.md`, polecenia w `development.md`, a bieżące braki
i rozbieżności w `implementation-status.md`. W innych miejscach odsyłaj do nich.

Po zmianie architektury, konfiguracji, kontraktu lub sposobu pracy aktualizuj
właściwy dokument. Po zmianie istotnego stanu implementacji aktualizuj jego opis
i datę weryfikacji. Nie dopisuj wyników testów, których nie wykonano.

Materiały źródłowe w `doc/` pozostają dostępne przez odnośniki w dokumentach
tematycznych. Otwarte decyzje produktowe wymagają ustalenia w ramach zadania;
ten zestaw nie rozstrzyga ich przez domysł.
