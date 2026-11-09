# Odświeżanie profili Tink w istniejącej bazie

[`tink_profiles.py`](../tink_profiles.py) czyta mapowanie JSON, tworzy świeżego
syntetycznego użytkownika Tink i jego dane dla każdego profilu, a po weryfikacji
zastępuje `user.user.tink_user_id` wskazanego użytkownika 4Money.
Nie uruchamia SQL init, migracji ani seedera bazy. Nie wymaga działającego API.

## Przygotowanie i mapowanie

Baza PostgreSQL musi działać i mieć zastosowany schemat/migracje. Wszystkie
wskazane konta muszą istnieć, być aktywne i mieć potwierdzony e-mail.
Przed pierwszym zapisem do Tink skrypt sprawdza wszystkie cele.

Użyj środowiska [Connector](../tink_connector/README.md):

```bash
uv sync --project tools --group pre-commit --group connector --locked
```

W ignorowanym `tools/.env` ustaw `Tink__ClientId`, `Tink__ClientSecret` oraz
`FOURMONEY_DATABASE_DSN` (format PostgreSQL/libpq). DSN musi wskazywać lokalny
serwer: localhost, 127.0.0.1 lub ::1. Opcjonalny `Tink__ApiBaseUrl` domyślnie
wskazuje `https://api.tink.com`. Używaj aplikacji testowej Tink z uprawnieniami
Connector. Klient Tink musi być ten sam co w backendzie korzystającym z mapowań.
Sekretów i DSN nie wpisuj do JSON ani argumentów CLI.

Domyślne mapowanie to [`users.json`](users.json), początkowo dla `postman.banking`:

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

`fourMoneyUserId` wybiera dokładnie `user.user._id`. Skrypt nie dopasowuje
profili po nazwie, loginie ani e-mailu i nie tworzy brakujących kont lokalnych.
`key` to unikalny identyfikator wpisu (1–64 znaków ASCII: litery, cyfry, `_`, `-`,
pierwszy znak alfanumeryczny). GUID i klucz nie mogą się powtarzać.
`preset` domyślnie wynosi `basic-pln`; własna ścieżka JSON jest względem pliku
mapowania. [Format presetu](../tink_connector/README.md#własny-preset-json).

## Polecenia z roota repozytorium

Po edycji domyślnego mapowania nie trzeba podawać danych profili w komendzie:

```bash
# Offline: walidacja JSON i podgląd; bez bazy, HTTP i zapisu plików.
uv run --project tools --group pre-commit --group connector --locked \
  python tools/scripts/tink_profiles.py plan

# Nowy batch: świeże tożsamości i dane Tink, aktualizacja wskazanych mapowań.
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/tink_profiles.py apply

# Własne mapowanie i jawny identyfikator batcha.
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/tink_profiles.py apply \
  --mapping path/to/users.json --run-id demo-profiles

# Wznowienie tego samego batcha po błędzie.
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/tink_profiles.py resume demo-profiles
```

`plan` i `apply` przyjmują `--mapping`, `--run-id` i `--as-of-date YYYY-MM-DD`.
Bez ID generują nowe; data domyślnie pochodzi z UTC. `apply` i `resume` obsługują
`--runs-dir`; przy wznowieniu wskaż ten sam katalog. Pomoc: `--help`.

Nowy `apply` tworzy nowe tożsamości `4money-test-*`, również gdy lokalne konto
ma już Tink ID. To odróżnia go od `tink_connector seed --four-money-user-id`,
który używa stałej tożsamości `4money-<GUID>` i może odzyskać istniejącego użytkownika.
Poprzednie zasoby w Tink nie są usuwane. Ten sam run ID wymaga `resume`.

## Zapis i wznowienie

Manifest główny jest w `.cache/tink-profiles/<runId>/manifest.json`, a manifesty
Connector i kopie presetów obok niego. Zawierają zamrożone profile, datę, ID,
postęp i fingerprinty środowiska; nie zawierają sekretów. Zachowaj cały katalog.
`resume` nie czyta ponownie pliku mapowania ani źródłowych presetów i odrzuca
zmianę bazy, klienta Tink lub adresu API. Nie poprawiaj ręcznie checkpointów.

Profile są przetwarzane kolejno. Każde mapowanie ma własną transakcję i używa
istniejącej blokady użytkownika, wspólnej z provisioningiem backendu.
Aktywność i potwierdzenie e-maila są sprawdzane ponownie przy zapisie.
Blokada sesyjna bazy wyklucza równoległy batch lub `reset_state`; blokady wierszy
nie są utrzymywane podczas wywołań Tink. Pozostali klienci bazy mogą nadal działać.

Pierwszy błąd zatrzymuje batch. Wcześniejsze mapowania pozostają zatwierdzone;
błąd ingestu lub wiązania pozostawia starą wartość niedokończonego profilu.
Po naprawie przyczyny użyj `resume`: odtworzy wynik z zapisanych manifestów,
bez tworzenia kolejnej tożsamości. Zapis zakończony przed utratą checkpointu
jest obsługiwany przez idempotentne ponowienie wiązania. Niepewne operacje zdalne
zachowują reguły Connector, w tym odmowę ponownego zapisu niepewnych transakcji.
Jeżeli mapowanie zakończonego wpisu zmienił inny proces, wznowienie odmawia
wykonania (`profiles_binding_changed`).

Stdout po sukcesie zawiera JSON z manifestem i mapowaniami, stderr — postęp lub
bezpieczny kod błędu, ścieżkę manifestu i polecenie wznowienia. Kody wyjścia:
0 sukces, 1 błąd, 130 przerwanie. Ponowne przypisanie Tink ID należącego już do
innego konta jest odrzucane przez istniejącą unikalność w bazie.

Zmienia się tylko `tink_user_id` wybranych kont. Istniejące rachunki, transakcje,
sesje i połączenia bankowe pozostają. Połączenia zachowują poprzednią tożsamość;
nowa tożsamość potrzebuje własnego Link/callback do testowania połączenia z bankiem.
Import syntetycznych rachunków/transakcji uruchom osobno przez Banking refresh.
Batch nie potwierdza zgód ani uprawnień produktu. Pełny reset danych zapewnia
osobny [`reset_state`](../reset_state/README.md).

## Testy lokalne

```bash
uv run --project tools --group pre-commit --group connector --locked \
  python -m unittest discover -s tools/scripts/tests -v
```

Testy profili i powiązań używają `FOURMONEY_TEST_POSTGRES_DSN` do tworzenia własnych
jednorazowych baz na lokalnym serwerze testowym. Bez tej zmiennej przypadki
PostgreSQL są pomijane. Testy regresji resetu wymagają dodatkowo
`FOURMONEY_TEST_MIGRATIONS_SQL` wskazującego wygenerowany skrypt migracji EF.
Szczegóły środowiska testowego opisuje [reset_state](../reset_state/README.md).
HTTP trafia do stanowej atrapy Tink. Testy nie wymagają prawdziwych sekretów
ani dostępu do Sandbox i nie potwierdzają działania rzeczywistego Tink.
