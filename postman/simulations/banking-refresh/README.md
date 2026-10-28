# Refresh on demand — lokalna symulacja

Backend 4Money i jego worker działają naprawdę. Jedyną symulowaną zależnością
HTTP jest Tink. PostgreSQL, uwierzytelnianie, migracje i zapis danych są rzeczywiste.
Nie są wymagane konto Postmana ani poświadczenia Tink. Nie publikujemy mocka.

Wymagania: .NET 10 z dostępnymi zależnościami i `dotnet ef`, Node 18+, OpenSSL,
`psql`, Docker oraz Postman CLI 1.76+. W tym środowisku `/usr/bin/postman` jest
Desktopem; CLI znajduje się w `~/.local/bin/postman`. Można wskazać je przez
`POSTMAN_CLI`. Wszystkie polecenia wykonuj z katalogu głównego repozytorium.

## Przygotowanie

Poniższy kontener ma osobny port, syntetyczne poświadczenia i dane w tmpfs.
Nie korzysta z lokalnej bazy deweloperskiej na porcie 5434.

```bash
docker run --name zpi-postman-refresh-tests --rm -d \
  --tmpfs /var/lib/postgresql/data \
  -e POSTGRES_USER=refresh_tests -e POSTGRES_PASSWORD=refresh_tests_local \
  -e POSTGRES_DB=refresh_tests -p 127.0.0.1:55439:5432 postgres:15-alpine

dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --no-restore -m:1
node postman/support/refresh-local.cjs cert
# Zaczekaj na gotowość PostgreSQL:
PGPASSWORD=refresh_tests_local pg_isready -h 127.0.0.1 -p 55439 -U refresh_tests
node postman/support/refresh-local.cjs setup
```

`setup` odmawia pracy, jeśli tabela rachunków już istnieje. Inicjalizuje wyłącznie
stałą bazę `refresh_tests` na porcie 55439, stosuje migracje, seeder i mapowanie
`postman.banking → refresh-fixture-owner`. Nie jest poleceniem resetowania
istniejącej bazy. W razie nieudanego setupu odtwórz wyłącznie ten kontener testowy.

**EF i `.env`:** startup API wywołuje `Env.Load()`. Samo ustawienie
`DB_LOCAL_CONNECTION_STRING` przed `dotnet ef database update` nie wystarcza,
ponieważ `.env` może je nadpisać. Helper przekazuje jawne `--connection` do EF.
Backend jest uruchamiany z czystego katalogu roboczego, z jawnymi ustawieniami
procesu. Nie uruchamiaj migracji testowych bez tego zabezpieczenia.

## Uruchomienie

W trzech terminalach:

```bash
node postman/support/refresh-local.cjs simulation
node postman/support/refresh-local.cjs tls
node postman/support/refresh-local.cjs backend
```

Symulacja obsługuje `http://127.0.0.1:4900/tink`, osłona HTTPS
`https://localhost:7443/tink/`, a backend `https://localhost:7444`.
Certyfikat i klucz trafiają tylko do ignorowanego `.cache/banking-refresh`.
Zaufanie do certyfikatu jest przekazywane procesowi backendu i CLI; nie używamy
`--insecure`, nie instalujemy certyfikatu globalnie i nie zmieniamy kodu backendu.

W czwartym terminalu:

```bash
node postman/support/refresh-local.cjs run 40 50 60 70 71 72 73 74
node postman/support/refresh-local.cjs restart-test
node postman/support/refresh-local.cjs verify
```

`run` korzysta z właściwej kolekcji Banking, Delay=2000 ms, jednej iteracji,
limitów czasu i `--no-report-events`. Logi oraz środowisko z JWT są zapisywane
lokalnie, z prawami 0600. Nie udostępniaj `.cache/banking-refresh` ani raportów
z poświadczeniami. CLI bez logowania może wyświetlić komunikat o braku możliwości
publikacji wyników — lokalne asercje nadal są wykonywane.

Helper przygotowuje tokeny dodatkowych użytkowników bez zmiany Bearera właściciela.
Folder 50 porównuje również snapshot SQL istniejących transakcji przed i po runie;
uruchom najpierw 40, aby snapshot zawierał zaimportowane rekordy.

| Folder | Sprawdzane zachowanie |
| --- | --- |
| 40 | Login → POST 202 → ograniczony polling → końcowy sukces |
| 50 | Dwa kolejne zadania, stałe lokalne ID kont, zero dodanych/zmienionych transakcji |
| 60 | POST/GET bez tokenu: 401; cudze i nieistniejące zadanie: 404; brak mapowania: 409 |
| 70 | Jedno konto odrzucone przez Tink 403, wynik PARTIALLY_SUCCEEDED |
| 71 | 429 z Retry-After dla B, retry bez ponownego pobierania A |
| 72 | Niepoprawna lista kont, FAILED przed uzgodnieniem kont |
| 73 | Kompletna pusta lista kont, SUCCEEDED z zerowymi licznikami |
| 74 | Zablokowana odpowiedź, ponowny POST zwraca to samo aktywne zadanie |
| 75/76 | Wznowienie po przerwaniu procesu, zachowany sukces pierwszego konta |

`restart-test` uruchamia 75, sprawdza PID, ścieżkę programu i połączenie z bazą
we własnym procesie testowym, po czym wysyła SIGKILL wyłącznie do niego.
Uruchamia zastępczy backend z tą samą konfiguracją, wykonuje 76 i zatrzymuje
zastępczy proces. PID innego programu lub połączenie z inną bazą powodują odmowę.
Po tym poleceniu backend testowy jest zatrzymany; do kolejnych runów uruchom go ponownie.

## Postman Desktop i ręczny restart

Importuj kolekcję Banking i szablon środowiska Local. Dla prawdziwego Sandboxu
używaj folderu 40, a 50 tylko przy pewności, że dane pozostają niezmienne.
Foldery 70–76 wymagają lokalnej atrapy. JWT, hasła i ID pozostają lokalne.

Dla fixture ustaw `baseUrl=https://localhost:7444`, użytkownika `postman.banking`
z hasłem seedera oraz `bankRefreshMockBaseUrl=http://127.0.0.1:4900/tink`.
Dodatkowe tokeny w folderze 60 należą do dwóch innych aktywnych użytkowników:
`seed_anna` i `seed_celina`; obaj mogą nie mieć mapowania Tink. Uzyskaj je przez
istniejący request logowania, zachowaj osobno i przywróć dane właściciela.
Dodaj lokalny certyfikat jako zaufany certyfikat CA w ustawieniach Postmana.

Runner: jedna iteracja, **Delay=2000 ms**. Limity to
`bankRefreshTimeoutSeconds=2400`, `bankRefreshMaxPolls=1200`.
`bankRefreshExpectedStatus=SUCCEEDED`, `bankRefreshMinAccounts=1` dotyczą głównego
flow; scenariusze błędów mają własne stałe oczekiwania. Błąd HTTP lub kontraktu
zatrzymuje run. Wygaśnięcie czasu nie jest dowodem statusu FAILED zadania.
Pojedynczy Send pozostaje ręcznym odczytem; automatyczna pętla działa w Runnerze.

Ręczny restart: uruchom 75, po jego zakończeniu od razu przerwij backend testowy
(najlepiej wymuszonym zakończeniem procesu), uruchom go z tą samą bazą i konfiguracją,
a następnie uruchom 76. Nie resetuj atrapy i nie twórz nowego zadania. Blokowane
jest konto przetwarzane jako drugie — kolejność lokalnych UUID nie jest założeniem.
Reaguj przed wyczerpaniem trzech prób workera; timeout pojedynczego wywołania Tink
wynosi 30 sekund. Sam folder 76 nie dowodzi restartu, jeśli proces nie został przerwany.

## Potwierdzenie SQL

```bash
PGPASSWORD=refresh_tests_local psql -h 127.0.0.1 -p 55439 -U refresh_tests -d refresh_tests \
  -v user_id=10000000-0000-0000-0000-000000000010 -v refresh_id=ID_ZADANIA \
  -f postman/support/refresh-verify.sql
```

Skrypt wykonuje transakcję tylko do odczytu i filtruje dane po użytkowniku oraz
zadaniu. Porównaj lokalne ID transakcji przed i po ponownej synchronizacji,
liczniki, status Booked, kwoty -12.34 i 770 PLN oraz oba znaczniki synchronizacji.
Po 70 sprawdź, że znacznik nieudanego konta nie przesunął się. Po 72 konta nie
powinny być zamknięte; po 73 wcześniej odkryte konta bieżącego mapowania powinny
mieć IsClosed=true. Kolejny udany refresh otworzy je ponownie.
`verify` automatycznie sprawdza cztery unikalne transakcje fixture oraz wykluczenie
operacji sprzed 45 dni. Nie potwierdza integracji z rzeczywistym bankiem.

## Testy skryptów kolekcji

```bash
node tools/scripts/tests/test_banking_collection_login.cjs
node tools/scripts/tests/test_banking_refresh_collection.cjs
```

To testy skryptów na syntetycznych odpowiedziach, odrębne od runów backendu.

## Zatrzymanie

Zatrzymaj terminale symulacji, TLS i backendu przez Ctrl+C, następnie:

```bash
docker stop zpi-postman-refresh-tests
```

Kontener usuwa się automatycznie. Powyższe polecenie dotyczy tylko kontenera fixture.
