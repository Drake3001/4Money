# Kontekst dla agentów: 4Money

4Money wspiera finanse osobiste i współdzielone: wydatki ręczne, paragony,
transakcje bankowe, analizę produktów i podział kosztów. Wymagania i stan wykonania
są odrębne; katalog lub model nie dowodzi ukończenia funkcji.

## Jak rozpocząć sesję

1. Ustal zakres, sprawdź `git status` i bieżące pliki. Zachowaj zmiany użytkownika.
2. Z tabeli wybierz potrzebne **sekcje**, następnie dokumentację danego modułu.
   Nie czytaj całego zestawu kontekstu przy każdym zadaniu.
3. Przed zmianą zachowania porównaj wymagania z kodem i testami. Rozbieżności
   nazwij; nie dopasowuj wymagań do implementacji ani nie zgaduj decyzji produktu.
4. Dobierz weryfikację do zmiany i raportuj wyłącznie wykonane kontrole.

| Zadanie | Punkt wejścia i dalsza lektura |
| --- | --- |
| Wymagania, MVP, UI | [Produkt](agent-context/product.md), właściwy wiersz [statusu](agent-context/implementation-status.md); dla web [frontend](frontend/README.md) |
| Backend / API | Właściwe pojęcia [domeny](agent-context/domain.md), granice [architektury](agent-context/architecture.md), README modułu |
| Konto, sesje, Google, SMTP | [User](backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md) |
| Banking / import | [Banking](backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md), [diagnostyka](agent-context/development.md#diagnostyka-banking) |
| Grupy / rozliczenia | [Grouping](backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/README.md), [transakcje](agent-context/architecture.md#transakcje-między-modułami) |
| Budżety / alerty | [Limits](backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md), [budżety](agent-context/domain.md#budżety) |
| Notifications / inbox | [Powiadomienia](agent-context/domain.md#powiadomienia), [testy](backend/ZPI4M.Backend/tests/README.md), [Postman](postman/README.md) |
| OCR / kategoryzacja | [Paragony](agent-context/domain.md#paragony-i-ocr), [worker](workers/extraction_worker/README.md); przy ewaluacji [eksperymenty](workers/extraction_worker/experiments/README.md) |
| Modele / migracje | [Persystencja](agent-context/architecture.md#persystencja), [migracje](agent-context/development.md#modele-i-migracje), konfiguracje EF i SQL |
| Testy / hooki / CI | [Kontrole](agent-context/development.md#kontrole-backendu), [narzędzia](tools/README.md), workflow dotyczący zadania |
| Infrastruktura | [Proxmox](proxmox-infra/README.md), właściwy workflow; zasady lokalne w [development](agent-context/development.md) |

## Orientacja w repozytorium

| Miejsce | Zawartość |
| --- | --- |
| `backend/ZPI4M.Backend/` | Moduły .NET, host API, migracje, testy |
| `frontend/` | React/TypeScript/Vite, widoki i klient API |
| `workers/extraction_worker/` | FastAPI/Python/uv, OCR, kategoryzacja, eksperymenty |
| `containers/local/` | Compose pięciu usług, wspólne `.env` i szablon |
| `database/` | Historyczny SQL init i destrukcyjny seed syntetyczny |
| `postman/`, `tools/` | Kolekcje, środowiska, hooki, kontrole CI i narzędzia |
| `agent-context/` | Krótki kontekst tematyczny; bieżące braki w statusie |
| `notes/` | Szczegółowe kontrakty i instrukcje; datowane eksperymenty OCR |
| `proxmox-infra/`, `.github/workflows/` | Terraform i automatyzacja infrastruktury/CI |
| `../doc/` | Wymagania TXT i diagramy poza repozytorium; nie są kodem runtime |

## Obowiązujące zasady

- Zachowuj modularny monolit i granice warstw. Tożsamość pobieraj przez
  `ZPI4M.Shared.CurrentUser.ICurrentUser`; `HttpCurrentUser` jest wspólny i scoped.
  Nie parsuj ponownie `sub`, nie twórz adapterów modułowych; brak ID daje 401.
  Nie zastępuje to walidacji JWT ani autoryzacji operacji.
- Wydatek, pozycja, paragon i transakcja bankowa są różnymi pojęciami.
  Aplikacja prezentuje zobowiązania; nie wykonuje przelewów, nie przechowuje
  środków ani nie udziela doradztwa finansowego. Push jest poza bieżącym zakresem.
- Przy zapisie między modułami stosuj [checklistę transakcji](agent-context/architecture.md#checklista-zapisu-między-modułami)
  i testy rollbacku całej operacji. Wspólne połączenie nie zapewnia transakcji;
  wywołany moduł nie zatwierdza transakcji koordynatora.
- Nie zapisuj sekretów, tokenów ani rzeczywistych danych finansowych w kodzie,
  logach lub dokumentacji. Przykłady i dane testowe mają być syntetyczne.
- Operacje na bazie i infrastrukturze wykonuj tylko w zakresie zadania.
  Seed usuwa dane i nie naprawia schematu. Odtworzenie wolumenu lub czyszczenie
  danych wymaga upoważnienia w danym zadaniu; nie obchodź osłon migracji.
- Przestrzegaj `backend/.editorconfig`. Formatowanie nie zastępuje kompilacji
  i testów. CI wybiera projekty według nazw i śledzenia w Git; sprawdzaj, co uruchomił.
  Konwencje commitów: [ClickUp](tools/README.md#clickup-branch-and-commit-conventions).

## Utrzymanie kontekstu

Produkt jest właścicielem zakresu wymagań, domena pojęć i niezmienników,
architektura wzorców integracji, development sposobu pracy, a status możliwości
oraz braków. Szczegóły kontraktów i procedur utrzymuj w dokumentacji modułów.
W innych miejscach dodaj odsyłacz zamiast kolejnej pełnej kopii.

Aktualizuj odpowiedni dokument po zmianie kontraktu, konfiguracji lub architektury.
W statusie podaj podstawę przeglądu i zakres faktycznej weryfikacji. Nie dopisuj
historii każdej sesji, porzuconych wariantów ani wyników bez ich wykonania.
Historia pozostaje w Git; zachowaj krótkie uzasadnienie tylko wtedy, gdy chroni
aktualną decyzję lub ograniczenie. Otwarte decyzje rozstrzygaj w ramach zadania.
