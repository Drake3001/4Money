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
| Wymagania, MVP, UI web/mobile | [Produkt](agent-context/product.md), [domena](agent-context/domain.md), [status](agent-context/implementation-status.md); dla web także [frontend](frontend/README.md) i [uruchomienie](agent-context/development.md#frontend--uruchomienie-i-kontrole) |
| Backend i kontrakty API | [Domena](agent-context/domain.md), [architektura](agent-context/architecture.md), [praca w repo](agent-context/development.md), właściwy obszar [statusu](agent-context/implementation-status.md) |
| Banking i import transakcji | Dokumenty backendowe powyżej oraz [kontrakt Banking](backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md) |
| Notifications i inbox | Dokumenty backendowe powyżej oraz [kolekcje Postman](postman/README.md) i [testy backendu](backend/ZPI4M.Backend/tests/README.md) |
| OCR i kategoryzacja | [Produkt](agent-context/product.md), [domena](agent-context/domain.md), [architektura](agent-context/architecture.md), [status](agent-context/implementation-status.md), [worker](workers/extraction_worker/README.md) i w razie ewaluacji [eksperymenty](workers/extraction_worker/experiments/README.md) |
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
| `frontend/` | React + TypeScript + Vite i Tailwind; ekrany i klient API, proxy developerskie; ograniczenia w statusie implementacji |
| `containers/local/` | Jeden Compose frontendu Vite, PostgreSQL, backendu, SeaweedFS i OCR; wspólny .env i .env.example; instrukcja uruchomienia |
| `containers/deploy/` | Miejsce na przyszłą konfigurację wdrożenia; obecnie tylko .gitkeep |
| `database/init/` | SQL inicjalizujący bazę przed zastosowaniem migracji |
| `database/seed_local.sql` | Reset i syntetyczne dane lokalne; uruchamiaj po migracjach |
| `postman/` | Kolekcje User, Banking, Notifications, bezpośredniego dostępu S3 i diagnostyki Tink; szablony środowisk |
| `workers/extraction_worker/` | Osobny projekt Python/uv: API FastAPI, OCR i kategoryzacja pozycji; testy i eksperymenty |
| `tools/` | Narzędzia Python/uv, hooki i pomocnicze kontrole backendu |
| `.github/workflows/` | CI backendu oraz planowanie i wdrażanie infrastruktury |
| `notes/` | Instrukcje migracji, hooków i Tink; w `notes/ocr/` opis pipeline i historyczne wyniki eksperymentów |
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

## Orientacja w implementacji — 2026-11-17

Podstawa: przegląd rewizji `fe2e8c0`, aktualizacje do `c6e5b96` i lokalne zmiany
frontendu z 2026-11-16/17. Nie jest to nowa regresja całego systemu. Szczegóły i datowane wyniki
weryfikacji są w [statusie implementacji](agent-context/implementation-status.md).
Obecność integracji nie potwierdza działania rzeczywistych usług zewnętrznych.

- Frontend React 19/TypeScript/Vite 8/Tailwind 4 ma klienta API i widoki konta,
  grup, zaproszeń, rozliczeń, Expense/OCR, Banking i inboxa. `src/mocks/` zawiera
  typy i pomocniki, nie symuluje HTTP. Mobile/Capacitor nie ma jeszcze projektu.
  Pulpit bankowy nie oznacza implementacji Reports ani Limits.
- Klient HTTP zapisuje sesję w localStorage, współdzieli odświeżenie po 401
  i obsługuje wspólny dialog 501. Google Identity Services pobiera clientId/nonce
  z API; ID token pozostaje w pamięci. Domyślne proxy `/api` nie wymaga CORS;
  jawne VITE_API_BASE_URL przy innym origin wymaga konfiguracji CORS backendu.
- Expense obsługuje historię, szczegóły, tworzenie, preview, edycję i usuwanie
  z kontrolą Version oraz datą YYYY-MM-DD. Formularz UI rozdziela udziały także
  kwotą/suwakiem i blokadami; API utrwala wyłącznie procenty. Kategorie pochodzą
  z API, systemowe „Nieznane” nie jest wybierane dla ręcznej pozycji.
  Frontend wymaga dla „Zniżki” ujemnej ceny, ilości 1 i rabatu 0; wybranie tej
  kategorii ustawia ilość i rabat. Są to reguły UI, nie nowy kontrakt backendu.
  Powiązanie bankowe rozpoznaje po ID, niezależnie od dostępu do szczegółów:
  użytkownik inny niż płatnik widzi właściciela, bez przycisków zmiany/odłączenia transakcji;
  płatnik i kwota są zablokowane, a zapis zachowuje ID oraz kwotę wydatku.
- Expense, Grouping, Notifications, Receipt i scoped BankingDbContext
  współdzielą NpgsqlConnection. Transakcję rozpoczyna i zatwierdza koordynator
  Grouping; konteksty dołączaj jawnie i wykonuj operacje kolejno. Fabryka
  Banking dla prób/połączeń, User i Limits używają osobnych połączeń.
  Zastosuj [checklistę transakcji](agent-context/architecture.md#do-wykonania-przy-implementacji-expense-i-grouping)
  i testy rollbacku przy rozszerzaniu zapisów między modułami.
- Zapisy Expense atomowo utrzymują udziały, TotalPaid/TotalShare i rejestr
  BankTransactionAssignment; utworzenie dodaje ExpenseAdded dla pozostałych
  aktualnych członków. Edycja/usunięcie nie powiadamiają. GroupSettlement nadal
  odrzuca nowe bankTransactionId; odbudowa agregatów pozostaje do wykonania.
- Grouping ma API grup, ikon, członkostw, zaproszeń, rozliczeń i sugestii.
  Lista grup sortuje po name/joinedAt, członkowie mają opcjonalne displayName
  z lookupu User. Archiwizacja i opuszczenie zachowują historię.
- Notifications ma NotifyAsync i autoryzowany inbox z paginacją, filtrem oraz
  odczytem pojedynczym/zbiorczym; klient webowy jest podłączony. NotifyAsync
  przez IUserLookupService filtruje aktywne konta i zwraca Guid? (null bez
  zapisu przy braku odbiorców). Grouping generuje ExpenseAdded,
  GroupMemberJoined i SettlementReceived. Limits nie generuje alertów.
  FK Notification → Expense mapuje AppDbContext; Notifications nie importuje Expense.
- Banking ma Tink, odczyty lokalne i trwały worker refreshu. UI otwiera Tink
  w nowej karcie i odpytuje status w pierwotnej. Callback z Accept: text/html
  zwraca samodzielną stronę wyniku; klient JSON zachowuje DTO/ProblemDetails.
  Brak odłączania banku i odnawiania zgód. Sprawdzaj rzeczywistą konfigurację
  ContinuousAccessVerified: appsettings i szablon Compose mają true, fallback
  Compose false; to ustawienie nie dowodzi uprawnień ani weryfikacji Tink.
  Refresh pokazuje polskie opisy błędów; identyczne alerty żądań w Banking
  są scalane. `BANKING_TINK_UNAVAILABLE` obejmuje także błędy lokalnej
  konfiguracji, np. continuous_access_unverified; sam kod nie dowodzi awarii Tink.
- User ma login lub e-mail, sesje, Google, pierwsze hasło konta Google,
  profil, zdjęcie, zmianę e-maila, reset i dezaktywację. Nie łączy kont po e-mailu
  i blokuje zmianę adresu konta Google. Zmiana hasła w UI używa resetu przez
  e-mail; brak endpointu zmiany przez podanie obecnego hasła.
- Wymagany FRONTEND_URL zasila linki e-mail i jest walidowany przy starcie API.
  Szablony API/Compose podają publiczny adres frontendu. Wiadomości mają logo
  MIME CID i datę Europe/Warsaw. Szczegóły SMTP/commitu i wyjątek rejestracji
  Google opisuje [architektura](agent-context/architecture.md).
- User/Grouping współdzielą Shared.Images.IconImageProcessor (Magick.NET):
  PNG/JPEG/WebP/HEIC/HEIF, walidacja, orientacja, usunięcie metadanych i WebP
  do 512 × 512. Frontend przygotowuje obrazy lokalnie (źródło do 25 MiB),
  API nadal przyjmuje do 5 MiB. Storage i sprzątanie pozostają w modułach.
- Receipt nie ma osobnego kontrolera. Expense analizuje plik bez zapisu,
  zapisuje paragon z formularzem i opcjonalnym podpisanym ocrProof oraz odczytuje
  go po ID wydatku. Nie zakładaj atomowości PostgreSQL i S3; kompensacja jest
  best effort. OCR FastAPI domyślnie używa RapidOCR i Gemini przez OpenRouter;
  propozycje kategorii mapuje Expense, użytkownik poprawia formularz.
- Reports korzysta z IExpenseAggregationService dla czterech raportów;
  starszy serwis odczytu usunięto jako nieużywany 2026-11-18.
  Daty agregacji są obustronnie włączne; koniec historii HTTP jest wyłączny.
  Limits ma modele i repozytorium CategoryGroupLimit, bez usług/API limitów.
- Solucja ma siedemnaście projektów xUnit. Testy PostgreSQL wymagają wspólnej
  TEST_POSTGRES_CONNECTION_STRING; brak lokalnie oznacza pominięcie, w CI błąd.
  Ręczny test S3 wymaga STORAGE_INTEGRATION_TESTS=1. Workflow backendu nie
  uruchamia testów frontendu ani workera Python. Ostatnie zmiany frontendu:
  kontrola TypeScript zaliczona, bez uruchamiania testów, builda Vite i usług
  zewnętrznych. Historyczne wyniki w statusie nie potwierdzają tych zmian.
- Nowa baza: SQL init → migracje EF → seed. Ostatnia migracja to
  `20261116211631_NormalizeExpenseCategoriesAndAddDeposit`: normalizacja nazw
  kategorii i dodanie „Kaucji”. Poprzednia `20261109191600_UserImprovements` dodaje aktywny e-mail, GoogleSubject,
  IconStorageKey i trigger grupy „Wydatki osobiste” w PLN. Nie uzupełnia grup
  istniejącym kontom. Łańcuch i ostrzeżenia po rebase opisuje development.md;
  nie przywracaj zastąpionych migracji. Seed czyści dane i nie uploaduje plików.
  Odtworzenie wolumenu wymaga zgody na usunięcie danych w danym zadaniu.
- Pięć usług Docker korzysta wyłącznie z containers/local/.env. Compose ustawia
  wewnętrzne adresy zależności; STORAGE_PUBLIC_ENDPOINT służy podpisanym URL-om
  dla klienta. API wymaga także konfiguracji JWT, storage i dowodu OCR. Pliki
  .env API/workera dotyczą uruchomienia bez Dockera. Domyślny profil API HTTP
  używa portu 5283; dopasuj Postman do faktycznie uruchomionego profilu.

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
