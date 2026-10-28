# Reset lokalnego stanu SQL i Tink

[`reset_state.py`](../reset_state.py) łączy istniejący
[`database/seed_local.sql`](../../../database/seed_local.sql), Connector i zapis
`tink_user_id`. Wymaga lokalnej bazy ze wszystkimi migracjami EF oraz testowej
aplikacji Tink z dostępem do Connector Data v2. Nie uruchamia kontenerów ani
migracji. Na czas resetu zatrzymaj backend i inne procesy zapisujące do bazy;
blokada skryptu serializuje wyłącznie uruchomienia `reset_state`.

**`reset` usuwa dane aplikacji i sesje przez istniejący seeder SQL.** Następnie
tworzy świeże zasoby Tink i zapisuje ich ID do wskazanych użytkowników.
Poprzednie zasoby Tink pozostają. Ingest nie tworzy zgód ani połączeń bankowych
i nie importuje danych przez backend.

## Uruchomienie

Użyj środowiska uv opisanego w [instrukcji Connector](../tink_connector/README.md).
`tools/.env` musi zawierać `Tink__ClientId`, `Tink__ClientSecret` oraz
`FOURMONEY_DATABASE_DSN` w formacie libpq. DSN musi jawnie wskazywać localhost,
127.0.0.1 lub ::1. Nie podawaj sekretów w argumentach CLI.
`Tink__ApiBaseUrl` jest opcjonalny.

Z roota repozytorium:

```bash
# Offline: walidacja mapowania, bez plików, bazy i HTTP.
uv run --project tools --group pre-commit --group connector --locked \
  python tools/scripts/reset_state.py plan

# Pełny reset; brak --run-id generuje nowy identyfikator.
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/reset_state.py reset --run-id demo-reset

# Kontynuacja tego samego resetu po błędzie.
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/reset_state.py resume demo-reset
```

`plan` i `reset` przyjmują `--mapping PATH`, `--run-id ID` oraz
`--as-of-date YYYY-MM-DD` (domyślnie data UTC). `reset` i `resume` przyjmują
`--runs-dir PATH`; przy wznowieniu wskaż ten sam katalog. Ścieżki CLI są względem
bieżącego katalogu. Domyślne mapowanie i SQL są odnajdywane względem skryptu.

## Mapowanie

Domyślny [`users.json`](users.json) łączy tylko `postman.banking`:

```json
{
  "version": 1,
  "users": [
    {
      "fourMoneyUserId": "10000000-0000-0000-0000-000000000010",
      "key": "postman-banking",
      "preset": "basic-pln"
    }
  ]
}
```

`fourMoneyUserId` to UUID konta tworzonego przez SQL, a `key` to unikalna nazwa
wpisu: 1–64 znaków ASCII, litery/cyfry oraz `_` i `-`, pierwszy znak
alfanumeryczny. Nie wpisuj gotowego Tink ID: odczyta je Connector.
Opcjonalny `preset` domyślnie wynosi `basic-pln`; własna ścieżka jest względem
pliku mapowania. Format presetu opisuje instrukcja Connector. Nie można
powtarzać UUID ani kluczy; wymagany jest przynajmniej jeden wpis.

Można dodać `seed_anna` (UUID kończy się `0001`), `seed_bartek` (`0002`)
i `seed_celina` (`0003`). `seed_dawid` (`0004`) jest celowo nieaktywny
i niepotwierdzony. Wszystkie cele są sprawdzane po SQL, przed pierwszym
żądaniem Tink; nieistniejące lub niedostępne konto zatrzyma run.

## Odzyskiwanie

W ignorowanym `.cache/reset-state/<runId>/` znajdują się manifest zbiorczy,
zamrożone presety i manifesty Connector w `connector/<connectorRunId>/`.
Zachowaj cały katalog. Manifest zawiera mapowanie, datę, checksumę SQL, ID
i postęp; nie zawiera DSN, haseł ani tokenów. Zmiana wejściowego mapowania lub
presetu nie zmienia zapisanego runu.

Kolejność to SQL → sprawdzenie wszystkich użytkowników → dla każdego wpisu
Connector `seed` → `link-db`. Samodzielny `seed` tworzy unikalne zewnętrzne ID
`4money-test-*`; `seed --four-money-user-id` narzuciłby stałe zewnętrzne ID
konta, kolidujące z kolejnymi resetami tworzącymi świeżych użytkowników.

SQL zachowuje własną transakcję. Błąd przed COMMIT wycofuje reset SQL; błąd Tink
nie wycofuje zatwierdzonych danych lokalnych ani poprzednich powiązań runu.
Skrypt zatrzymuje się przy pierwszym błędzie z kodem niezerowym, ścieżką
manifestu i komendą wznowienia. `resume`:

- nie powtarza SQL po zapisanym potwierdzeniu jego sukcesu;
- może powtórzyć SQL po utracie checkpointu tylko przed rozpoczęciem Connector
  i przy niezmienionej checksumie skryptu;
- korzysta z checkpointów Connector i bezpiecznie ponawia binding;
- sprawdza zgodność bazy, klienta Tink i URL;
- odrzuca zmienione powiązanie już zakończonego wpisu.

Nie wznawiaj starego runu po rozpoczęciu kolejnego resetu tej samej bazy.
Niepewne zdalne zapisy nadal podlegają zasadom odzyskiwania Connector;
skrypt nie tworzy zastępczych runów, aby ponowić takie operacje.
Blokada plikowa chroni run, a sesyjna blokada PostgreSQL całą operację dla bazy.

## Testy

Testy offline nie wymagają konfiguracji. Testy PostgreSQL używają własnych
losowych baz i rzeczywistego HTTP do syntetycznej atrapy Tink. Przygotuj
odizolowany serwer testowy oraz aktualny build backendu, następnie wygeneruj
SQL migracji bez stosowania ich do bazy deweloperskiej:

```bash
dotnet ef migrations script --no-build \
  --project backend/ZPI4M.Backend/src/ZPI4M.Database \
  --startup-project backend/ZPI4M.Backend/src/ZPI4M.Api \
  --context AppDbContext --output /tmp/reset-state-migrations.sql
```

Ustaw lokalnie `FOURMONEY_TEST_POSTGRES_DSN` wskazujący serwer testowy z prawem
tworzenia baz i `FOURMONEY_TEST_MIGRATIONS_SQL=/tmp/reset-state-migrations.sql`.
Testy tworzą baseline z `database/init`, stosują SQL migracji, a potem wykonują
prawdziwy seeder. Usuwają wyłącznie własne bazy `reset_tests_*`.
Bez obu zmiennych nowe testy PostgreSQL są pomijane.

```bash
uv run --project tools --group pre-commit --group connector --locked \
  python -m unittest discover -s tools/scripts/tests -v
```

Testy nie wymagają prawdziwych danych logowania Tink. Lokalne wyniki nie
potwierdzają uprawnień aplikacji ani działania rzeczywistego Sandbox.
