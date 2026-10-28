# Syntetyczne dane Tink Connector Data v2

CLI [tink_connector.py](../tink_connector.py) tworzy testowego użytkownika Tink,
rachunki i transakcje, czeka na wynik operacji i weryfikuje zapis przez HTTP.
Domyślnie działa niezależnie od backendu 4Money i jego bazy. Opcjonalnie zapisuje
powiązanie użytkownika Tink z istniejącym kontem 4Money w lokalnym PostgreSQL.
Nie wymaga uruchomionego backendu ani auth 4Money.
Zakres obejmuje `plan`, `seed`, `resume`, `verify` oraz `link-db`; aktualizacja i usuwanie
zasobów pozostają poza zakresem. Ingest nie testuje Tink Link, SCA ani zgody bankowej.

Do jednego uruchomienia SQL seedera, utworzenia świeżych danych Tink i zapisania
powiązań użyj [`reset_state`](../reset_state/README.md). Mapowanie JSON domyślnie
obejmuje tylko `postman.banking`; manifest zbiorczy umożliwia wznowienie bez
powtarzania zatwierdzonego resetu SQL.

## Alias i szybki przegląd komend

W Bash lub Zsh możesz skrócić wspólny człon do aliasu `tink`. Po przygotowaniu
`tools/.env` ustaw w terminalu:

```bash
alias tink='uv run --project tools --group pre-commit --group connector --locked --env-file tools/.env python tools/scripts/tink_connector.py'
```

Alias działa z katalogu głównego repozytorium i przekazuje dopisane argumenty
do skryptu. Wczytuje konfigurację przez uv; nie uruchamia dodatkowego skryptu
Bash. Jeśli korzystasz ze zmiennych procesu zamiast pliku, usuń z definicji
`--env-file tools/.env` — wtedy również `plan` i pomoc nie wymagają tego pliku.

| Wywołanie po ustawieniu aliasu | Działanie |
| --- | --- |
| `tink plan` | Podgląd domyślnego presetu `basic-pln`, bez żądań do Tink i zapisu manifestu |
| `tink seed --run-id demo-pln` | Utworzenie testowego użytkownika, rachunku i transakcji oraz kontrola zapisu |
| `tink link-db demo-pln --four-money-user-id GUID` | Powiązanie ukończonego runu z lokalnym kontem 4Money |
| `tink resume demo-pln` | Wznowienie istniejącego runu na podstawie manifestu |
| `tink verify demo-pln` | Ponowny odczyt i porównanie danych; bez tworzenia zasobów w Tink |
| `tink verify demo-pln --hr` | Ta sama kontrola, dodatkowo czytelne podsumowanie obiektów i ID |
| `tink resume --manifest .cache/tink-connector/demo-pln/manifest.json` | Wznowienie ze wskazanego pliku |
| `tink --help` | Lista dostępnych komend |
| `tink seed --help` | Argumenty konkretnej komendy |

Definicja w terminalu obowiązuje w bieżącej sesji. Aby zachować ją na stałe,
dodaj tę samą linię do `~/.bashrc` (Bash) lub `~/.zshrc` (Zsh), a następnie
otwórz nowy terminal. Nadal wywołuj alias z roota tego repozytorium.

## Szybkie wznowienie istniejącego runu

Z katalogu głównego repozytorium, po uzupełnieniu `tools/.env`:

```bash
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env \
  python tools/scripts/tink_connector.py resume tink-demo-001
```

Zastąp `tink-demo-001` identyfikatorem swojego runu. Komenda odczyta
`.cache/tink-connector/tink-demo-001/manifest.json` i wznowi zapisany proces.
Aby wykonać wyłącznie kontrolę danych, zamień `resume` na `verify`.
Konfigurację pierwszego uruchomienia i wszystkie komendy opisano poniżej.

## Uruchomienie z roota repozytorium

Używamy tego samego projektu uv i `tools/.venv` co hooki. Grupa `connector`
dodaje `simplejson` (dokładne liczby Decimal w JSON), `filelock` (blokada runu),
Google Fire (CLI) oraz psycopg (opcjonalny zapis do PostgreSQL).
Zachowaj grupę `pre-commit`, aby synchronizacja nie usunęła narzędzi hooków:

```bash
uv sync --project tools --group pre-commit --group connector --locked
cp tools/.env.example tools/.env
```

Uzupełnij lokalny `tools/.env` danymi **testowej aplikacji Tink**:
`Tink__ClientId` i `Tink__ClientSecret`. Plik jest ignorowany przez Git.
Opcjonalny `Tink__ApiBaseUrl` domyślnie wskazuje `https://api.tink.com`.
Możesz zamiast pliku ustawić zmienne procesu; `--env-file` obsługuje uv,
nie własny parser skryptu. Sekret nie jest argumentem CLI ani polem presetu.

Uruchamiaj skrypt bezpośrednio przez uv, z katalogu głównego repozytorium.
`--project tools` wybiera wspólne środowisko `tools/.venv`, obie grupy zachowują
narzędzia hooków, a `--locked` sprawdza zgodność zależności z lockfile.
`--env-file tools/.env` przekazujemy do uv **przed** `python`; pomiń tę opcję,
jeżeli konfiguracja jest już w zmiennych procesu. Plik nie jest ładowany automatycznie.
Ścieżki argumentów, np. własny preset lub `--manifest`, są względem bieżącego katalogu.

## Zarejestrowane komendy CLI

[Google Fire](https://google.github.io/python-fire/guide/) rejestruje w `main()`
pięć komend skryptu. Nie są to globalne polecenia ani entry pointy pakietu uv;
wywołujemy je po `python tools/scripts/tink_connector.py`.

| Komenda | Co robi | Wymagania |
| --- | --- | --- |
| `plan` | Waliduje preset i pokazuje liczbę rachunków/transakcji, datę oraz external user ID. Nie wywołuje Tink i nie zapisuje manifestu. | Bez sekretów i `.env` |
| `seed` | Tworzy użytkownika testowego (lub weryfikuje wskazanego), zapisuje rachunki i transakcje, śledzi operację, sprawdza odczyt i utrwala ID. | Konfiguracja Tink i nowy run ID |
| `resume <runId>` | Odczytuje manifest i kontynuuje od zapisanego etapu, bez powtarzania niepewnych zapisów. | Istniejący manifest i ten sam klient/adres API |
| `link-db <runId> --four-money-user-id GUID` | Weryfikuje tożsamość Tink i zapisuje mapowanie przy istniejącym koncie lokalnym; bez ingestu. | Ukończony manifest, konfiguracja Tink i lokalny DSN PostgreSQL |
| `verify <runId>` | Odczytuje dane z Tink, porównuje z manifestem i zapisuje lokalny wynik kontroli. Nie tworzy zasobów w Tink. | Istniejący manifest i konfiguracja Tink |

### Podgląd i pomoc

```bash
uv run --project tools --group pre-commit --group connector --locked \
  python tools/scripts/tink_connector.py plan --preset basic-pln --run-id demo-pln --as-of-date 2026-10-23
uv run --project tools --group pre-commit --group connector --locked \
  python tools/scripts/tink_connector.py --help
uv run --project tools --group pre-commit --group connector --locked \
  python tools/scripts/tink_connector.py resume --help
```

### Utworzenie danych w Tink

```bash
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env \
  python tools/scripts/tink_connector.py seed --preset basic-pln --run-id demo-pln
```

Brak `--run-id` generuje nowy identyfikator. Brak `--as-of-date` używa dzisiejszej
daty UTC. Datę i wszystkie dane zamrażamy w manifeście przed pierwszym zapisem.
Nowy run tworzy nowego użytkownika i nowe zasoby. Ponowne `seed` z tym samym
run ID odmawia wykonania; do kontynuacji służy `resume`.

### Wznowienie i kontrola odczytu

```bash
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env \
  python tools/scripts/tink_connector.py resume demo-pln
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env \
  python tools/scripts/tink_connector.py verify demo-pln
```

Run ID wskazuje `.cache/tink-connector/<runId>/manifest.json` w repozytorium.
Dla własnego `--runs-dir` lub innej lokalizacji podaj manifest jawnie, zamiast run ID:

```bash
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env \
  python tools/scripts/tink_connector.py resume --manifest .cache/tink-connector/demo-pln/manifest.json
```

### Czytelne podsumowanie obiektów (`verify --hr`)

```bash
tink verify demo-pln --hr
```

Po poprawnej weryfikacji flaga dopisuje do stdout, po dotychczasowym JSON-ie,
podsumowanie: `user - created, verified - <Tink User ID>`, rachunki z ID,
transakcje z ID i statusem oraz operację. Dla wcześniej istniejącego użytkownika
pokazuje `existing`, a nie `created`. Zawiera też zewnętrzne ID i rozróżnia
salda sprawdzone i niezweryfikowane. `created` opisuje zasoby runu; samo `verify`
nie tworzy ich ponownie. Status operacji pochodzi z manifestu, nie z nowego
pollingu; nieznane `operationId` jest oznaczone jako `unknown`.

Bez `--hr` (lub przy `--hr=False`) stdout zachowuje dotychczasowy wynik JSON.
Logi diagnostyczne nadal trafiają na stderr. Przy nieudanej kontroli nie ma
podsumowania sukcesu. Flaga działa również z `--manifest` i pełnym wywołaniem uv.

### Opcje komend

| Opcja | Komendy | Działanie / domyślna wartość |
| --- | --- | --- |
| `--preset` | `plan`, `seed` | `basic-pln` albo ścieżka do własnego JSON |
| `--run-id` | `plan`, `seed` | Nowy identyfikator zestawu; domyślnie generowany |
| `--as-of-date` | `plan`, `seed` | Data bazowa `YYYY-MM-DD`; domyślnie dzisiejsza UTC |
| `--existing-external-user-id` | `plan`, `seed` | Wskazuje istniejącego użytkownika testowego zamiast tworzenia nowego |
| `--runs-dir` | `plan`, `seed` | Katalog manifestów; domyślnie `.cache/tink-connector` w repozytorium; `plan` nic w nim nie zapisuje |
| `<runId>` lub `--manifest` | `resume`, `verify` | Identyfikator runu albo jawna ścieżka; nie podawaj obu naraz |
| `--base-url` | Wszystkie | Nadpisuje `Tink__ApiBaseUrl`; domyślnie `https://api.tink.com` |
| `--http-timeout`, `--wait-timeout` | Wszystkie | Timeout żądania i pollingu w sekundach: `30`, `120`; w `plan` nie uruchamiają I/O |
| `--hr` | `verify` | Dopisuje czytelne podsumowanie potwierdzonych zasobów i ich ID; domyślnie `False` |
| `--help` | Wszystkie | Pomoc generowana przez Fire; bez wykonania komendy |

`verify` nie tworzy użytkownika, rachunków ani transakcji. Uzyskuje tokeny,
odczytuje dane i zapisuje lokalny wynik weryfikacji. `--existing-external-user-id`
dla `seed`/`plan` pozwala wskazać wyłącznie użytkownika z prefiksem `4money-test-`.
Jego tożsamość musi zostać potwierdzona odczytem przed dodaniem danych.
Nie podajemy dowolnego Tink User ID. Prefiks to konwencja narzędzia, nie dowód
izolacji środowiska — używaj klienta testowego.

## Co jest zapisywane i jak wznowić proces

Domyślnie `.cache/tink-connector/<runId>/` zawiera:

- `manifest.json`: datę, zamrożony preset i hash, adres API, fingerprint klienta,
  `externalUserId`, otrzymany `tinkUserId`, ID rachunków i transakcji, identyfikatory
  operacji, etapy oraz bezpieczny kod ostatniego błędu.
- `events.jsonl`: czas UTC, etap, metodę/ścieżkę HTTP, status, czas żądania
  i identyfikatory syntetycznych zasobów. Te same zdarzenia trafiają na stderr.
- `run.lock`: blokadę między procesami; drugi proces nie wykona tego samego runu.

Ścieżkę bazową zmienisz przez `--runs-dir`. Katalog `.cache` jest ignorowany
przez Git; zachowaj manifesty, jeśli chcesz wracać do utworzonych zasobów.
Manifest jest zapisywany atomowo (`fsync` + podmiana pliku). Zamiar zapisu
utrwalamy przed POST, a zwrócone ID przed kolejną operacją. Przy błędzie zapisu
manifestu stderr zawiera awaryjnie znane ID; proces zatrzymuje się.
Awaria pomocniczego logu daje ostrzeżenie na stderr i nie gubi odpowiedzi API.
Tokeny, kody OAuth, sekret i pełne odpowiedzi błędów nie trafiają do plików/logów.

Po przerwaniu użyj `resume` z tym samym klientem i adresem API:

- Niepewny wynik utworzenia użytkownika rozstrzygamy przez grant dla zapisanego
  `externalUserId` i `GET /api/v1/user`; nie tworzymy drugiego użytkownika.
- Niepewny wynik zapisu rachunków rozstrzygamy odczytem ich stabilnych ID.
  Brak rachunków kończy się timeoutem, bez automatycznego ponowienia POST.
- Jeśli znamy `operationId`, wznawiamy polling tej operacji. `PROCESSED` z
  niepustym `exceptions` nadal oznacza błąd. Sprawdzamy wszystkie wpisy `users`.
- Jeśli wynik POST transakcji jest nieznany i nie mamy `operationId`, `resume`
  zatrzymuje się z `unknown_transaction_write_requires_review`. Nie zakładamy,
  że `externalOperationId` zapewnia idempotencję. `verify` może potwierdzić
  widoczność danych, ale nie zastąpi wyniku operacji ani nie odblokuje zapisu.

Nie poprawiaj ręcznie checkpointów, aby wymusić powtórzenie ingestu.
Zmiana zamrożonych danych lub klienta powoduje odmowę wznowienia.
Nowy run jest nowym zestawem danych; narzędzie nie wykonuje cleanupu.
Sukces ma exit code `0`, błąd `1`, przerwanie `130`. Stdout na końcu zawiera JSON
ze ścieżką manifestu i ID; zdarzenia diagnostyczne są na stderr.

## Kontrakt HTTP i uprawnienia

Client tokeny są pobierane osobno dla minimalnych scope'ów: `user:create`,
`authorization:grant`, `accounts:write`, `transactions:write`. Do odczytu
używamy grantu `/api/v1/oauth/authorization-grant` dla `external_user_id`, wymiany
kodu na user token i scope'ów `user:read` oraz `accounts:read,transactions:read`.
Kontrola rachunków korzysta wyłącznie z listy `/data/v2/accounts`, bez osobnego
endpointu sald i bez scope'ów `accounts.balances:readonly` ani `balances:read`.
Tokeny istnieją wyłącznie w pamięci, są cache'owane według scope/użytkownika
z marginesem przed `expires_in`. Nie zapisujemy ani nie używamy refresh tokenów.
401 podczas odczytu powoduje jedną próbę z nowym tokenem. POST z danymi nie
jest automatycznie ponawiany. 400/401/403 kończą proces bez wypisywania body.
Dostęp klienta do Data v2 Connector i wymienionych scope'ów musi zapewnić Tink.
Brak uprawnień nie powoduje przełączenia na legacy Connector.

Zapis używa `/data/v2/connector/accounts`, `/data/v2/connector/transactions`
i `/data/v2/connector/operations/{operationId}`. Kontrola używa wszystkich stron
`/data/v2/accounts` oraz wszystkich
stron `/data/v2/transactions?accountIdIn=<wewnętrzne ID Tink>`.
Weryfikujemy nazwy/typy rachunków i salda dostępne na liście: `balances.booked`
porównujemy z przesłanym `bookedBalance`, a `balances.available` z
`availableBalanceIncludingCredit`. Kwoty i waluty muszą być zgodne. Brak pola
salda na liście nie blokuje ingestu. `creditLimit` nie jest zwracany przez tę
listę i nie jest kontrolowany. Manifest w `accountVerification` oraz zdarzenia
`account_balances_checked` rozróżniają `verifiedBalances` i `uncheckedBalances`;
sukces kontroli nie oznacza potwierdzenia pól niewystawianych przez listę.
Następnie weryfikujemy kwoty, waluty, statusy, opisy
oraz daty transakcji. Jeśli API zwraca timestamp beta, sprawdzamy także czas;
przy samym `dates` sprawdzamy datę kalendarzową.

`externalAccountId` oraz `externalTransactionId` są deterministycznymi UUID
z run ID i kluczy presetu. `externalOperationId` powstaje raz na run. Lista
transakcji nie dokumentuje pola `externalTransactionId`: w syntetycznych danych
wysyłamy więc także `reference` równe temu UUID i korelujemy odczyt po `reference`.
Nie zakładamy równości z wewnętrznym `id` ani z `providerTransactionId`.
Kwoty nie przechodzą przez float. Daty zapisu mają format UTC z milisekundami.

Domyślne timeouty: HTTP 30 s, polling 120 s; zmiana przez `--http-timeout` i
`--wait-timeout`. Polling ma backoff 1–10 s i obsługuje przejściowe błędy odczytu
429/5xx oraz sieci. Limit pollingu jest sprawdzany między żądaniami; rozpoczęty
odczyt może trwać do timeoutu HTTP. Paginacja ma własny limit 120 s/10000 stron
na odczyt listy i wykrywa powtarzający się token. Nie ma nieskończonych retry.
Przekierowania HTTP są odrzucane; HTTP bez TLS jest dozwolone tylko dla loopback
w testach. Nie wyłączamy walidacji certyfikatu HTTPS.

Aktualna referencja: [Tink Data v2](https://docs.tink.com/api-data-v2)
(sekcje Accounts, Connector Accounts/Transactions/Operations i Transactions),
[OAuth](https://docs.tink.com/api#oauth) i [Users](https://docs.tink.com/api#users).
Kontrakty sprawdzono dokumentacyjnie 2026-10-23. Dostęp konkretnej aplikacji
oraz zgodność odczytu `reference` i sald wymagają jeszcze uruchomienia z Tink;
lokalne testy nie potwierdzają aktywacji produktu.

## Własny preset JSON

Podaj ścieżkę zamiast nazwy: `--preset tools/scripts/tink_connector/presets/basic-pln.json`.
Punktem wyjścia jest [basic-pln.json](presets/basic-pln.json): jeden rachunek PLN,
wpływ, wydatek i transakcja oczekująca. Używaj wyłącznie danych syntetycznych.

| Pole | Znaczenie |
| --- | --- |
| `version`, `name`, `market`, `locale` | `1`, nazwa presetu, np. `PL`, `pl_PL` |
| `accounts[].key` | Unikalny klucz: litery/cyfry/`_`/`-`, maks. 64 znaki |
| `name`, `type`, `currencyCode` | Nazwa testowa, `CHECKING`/`SAVINGS`/`CREDIT_CARD`, kod ISO waluty |
| `minorUnit` | Obecnie wyłącznie `2`, zgodnie z ograniczeniem Connector v2 |
| `displayAccountNumber` | Opcjonalny syntetyczny numer/maska |
| `balances` | `bookedBalance`, `availableBalanceIncludingCredit`, opcjonalny `creditLimit`; stringi w minor units, np. `"125050"` = 1250,50 PLN |
| `transactions[].key` | Unikalny w rachunku klucz do wyliczenia UUID |
| `amount` | String dziesiętny w głównych jednostkach, np. `"-49.99"`; do API idzie jako liczba JSON |
| `description`, `status` | Syntetyczny opis oraz `BOOKED` lub `PENDING` |
| `dayOffset`, `timeUtc` | Całkowita liczba dni względem zamrożonej daty oraz opcjonalne `HH:MM:SS` (domyślnie `12:00:00`) |

Nieznane pola i niepoprawna precyzja są odrzucane przed żądaniami.
Pusta lista transakcji jest dozwolona. Salda są niezależnymi danymi testowymi;
skrypt nie wylicza ich jako sumy transakcji.

## Testy bez Tink i backendu

```bash
uv run --project tools --group pre-commit --group connector --locked \
  python -m unittest discover -s tools/scripts/tests -v
```

Testy uruchamiają rzeczywisty CLI jako podproces i lokalny, stanowy serwer HTTP
oparty na powyższym kontrakcie. Obejmują zapis/odczyt, paginację, Decimal, cache
tokenów, wznowienia, utratę odpowiedzi, timeout, częściowe błędy, 400/401/403,
ponowienie odczytu po 401/429, blokadę procesu i awarię persystencji. Nie używają
sekretów Tink ani testowego uwierzytelniania backendu. Podstawowy zestaw nie
wymaga PostgreSQL; dodatkowe testy powiązania opisano niżej.

## Powiązanie z kontem 4Money

Najpierw zastosuj migracje EF i uruchom `database/seed_local.sql` w lokalnej bazie.
Seeder resetuje dane i tworzy m.in. `postman.banking` z hasłem `TestPassword1!`,
aktywnym kontem i potwierdzonym e-mailem. Jego ID to
`10000000-0000-0000-0000-000000000010`. Następnie uzupełnij lokalny `tools/.env`:

```dotenv
FOURMONEY_DATABASE_DSN="host=localhost port=5434 dbname=zpi4m_db user=TWOJ_USER password=TWOJE_HASLO"
```

To standardowy DSN PostgreSQL/libpq. API używa innego formatu Npgsql; CLI nie
odczytuje automatycznie `.env` API. Dane połączenia muszą wskazywać tę samą lokalną
bazę. Obsługiwane hosty: `localhost`, `127.0.0.1`, `::1`. Hasła i DSN nie trafiają
do manifestów ani logów. Klient Tink skonfigurowany w CLI i API musi być ten sam.

Po ustawieniu aliasu `tink` opisanego wyżej:

```bash
# Podgląd pozostaje offline: nie wymaga bazy ani sekretów Tink.
tink plan --run-id demo-linked --four-money-user-id 10000000-0000-0000-0000-000000000010

# Zapis mapowania nastąpi automatycznie po pomyślnym seedowaniu i weryfikacji.
tink seed --run-id demo-linked --four-money-user-id 10000000-0000-0000-0000-000000000010

# Odtworzenie mapowania po resecie lokalnej bazy, również dla starszych runów.
tink link-db demo-linked --four-money-user-id 10000000-0000-0000-0000-000000000010

# Ponowienie po błędzie lokalnej bazy po sukcesie po stronie Tink.
tink resume demo-linked
```

Dla niestandardowej lokalizacji można zamiast run ID podać `--manifest`.
`link-db` wymaga ukończonego runu; odczytuje tożsamość z Tink, ale nie tworzy ani
nie ingestuje zdalnych zasobów. Powtarzanie tego samego mapowania jest dozwolone.
Nowe runy powiązane z bazą używają `externalUserId=4money-<GUID bez myślników>`,
zgodnie z backendem. Nie łącz `--four-money-user-id` z `--existing-external-user-id`.
Niezależne runy i dotychczasowe manifesty zachowują identyfikatory `4money-test-*`.
Opcjonalna sekcja `localBinding` w manifeście wersji 1 przechowuje ID i stan zapisu.
`verify` nadal sprawdza wyłącznie Tink i nie wymaga dostępu do lokalnej bazy.

CLI nie tworzy kont 4Money. Sprawdza istnienie, aktywność i potwierdzenie e-maila
przed zdalnymi zapisami oraz ponownie w transakcji końcowej. Zapisuje tylko
`user.user.tink_user_id`, używając blokady doradczej zgodnej z backendem.
Po sukcesie `seed`/`resume` aktualizuje także odmienne istniejące `tink_user_id`
wskazanego konta; ręczne wpisywanie ani czyszczenie ID nie jest potrzebne.
`link-db` również zastępuje mapowanie, po sprawdzeniu tożsamości ukończonego runu.
Brak konta nie powoduje jego utworzenia. Błąd seedowania pozostawia dotychczasowe
mapowanie. Przypisanie tego samego ID Tink do innego konta nadal daje
`local_mapping_conflict`; transakcja zachowuje poprzednią wartość. Niedostępna baza daje
`local_database_unavailable`. Run i zdalne ID pozostają w manifeście, a komenda
zwraca niezerowy kod. Wznów ją przez `resume` lub `link-db` po usunięciu przyczyny.

Zapis mapowania nie tworzy `BankConnection`, prób połączeń ani zgód. Po zalogowaniu
w Postmanie wykonaj normalny przepływ Banking/Tink Link i callback. Connector
przygotowuje syntetyczne dane oraz tożsamość; samo mapowanie nie potwierdza
Continuous Access, uprawnień produktu, SCA ani zgody bankowej.
Zmiana mapowania nie przepisuje istniejących połączeń Banking na nową tożsamość.
Odczyt zgód uwzględnia wyłącznie połączenia zgodne z aktualnym mapowaniem;
dla nowej tożsamości przejdź ponownie przepływ Link i callback.

### Testy powiązania z PostgreSQL

Testy uruchamiają CLI przez rzeczywisty HTTP do lokalnej atrapy Tink i tworzą
własne tymczasowe bazy z kontraktem tabeli użytkowników. Ustaw
`FOURMONEY_TEST_POSTGRES_DSN` na lokalny serwer z prawem tworzenia baz, a następnie
uruchom zwykły zestaw `python -m unittest discover -s tools/scripts/tests -v`.
Bez tej zmiennej testy PostgreSQL są pomijane. Nie używają `FOURMONEY_DATABASE_DSN`
jako serwera administracyjnego. Pełny SQL init, migracje, reset seedera i logowanie
wszystkich kont sprawdzają osobno testy .NET z `TEST_POSTGRES_CONNECTION_STRING`.
