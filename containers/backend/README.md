# Backend .NET w Dockerze

Compose uruchamia samo API. PostgreSQL, OCR i storage uruchom osobno lub wskaż
istniejące serwery w `backend/ZPI4M.Backend/src/ZPI4M.Api/.env`.
Nie wykonuje migracji bazy automatycznie.

Z katalogu głównego repozytorium przygotuj konfigurację (jeśli jeszcze nie istnieje):

```powershell
if (!(Test-Path containers/backend/.env)) {
    Copy-Item containers/backend/.env.example containers/backend/.env
}
```

`containers/backend/.env` zawiera wyłącznie ustawienia Compose: port, adres
publikacji i środowisko ASP.NET Core. Nie jest potrzebny do budowania obrazu.
Konfigurację aplikacji (baza, OCR, storage) Compose wczytuje przy uruchomieniu
z istniejącego `backend/ZPI4M.Backend/src/ZPI4M.Api/.env` przez `env_file`.
Plik ten musi istnieć; instrukcja jego uzupełnienia jest w README modułu Receipt.
Nie jest kopiowany do obrazu.

```powershell
docker compose --env-file containers/backend/.env -f containers/backend/docker-compose.yml config --quiet
docker compose --env-file containers/backend/.env -f containers/backend/docker-compose.yml up --build -d
docker compose --env-file containers/backend/.env -f containers/backend/docker-compose.yml logs -f backend
```

Domyślny adres API: `http://localhost:5283`. Zatrzymanie:

```powershell
docker compose --env-file containers/backend/.env -f containers/backend/docker-compose.yml down
```

Dokument OpenAPI jest dostępny pod `http://localhost:5283/openapi/v1.json`
wyłącznie w środowisku `Development`.
Domyślnie port API jest publikowany tylko na loopback hosta. Lokalnie uruchomione
API na porcie 5283 trzeba zatrzymać albo zmienić `BACKEND_PORT`.

## Połączenia z zależnościami

- [PostgreSQL](../docker-compose.yml) publikuje port `5434`.
- [OCR](../../workers/extraction_worker/compose.yaml) publikuje port `8000`.
- [Storage](../storage/docker-compose.yml) publikuje domyślnie port `8333`.

`localhost` wewnątrz kontenera oznacza kontener API. `host.docker.internal`
wskazuje host Dockera. Można go użyć w adresach bazy i OCR w `.env` aplikacji,
jeśli działają na hoście. Usługi muszą nasłuchiwać na adresie dostępnym
z kontenera; samo powiązanie portu z loopback hosta może nie wystarczyć,
szczególnie na Linuksie. Dostosuj adres publikacji OCR i storage do środowiska.

W `.env` aplikacji ustaw `STORAGE_ENDPOINT=http://host.docker.internal:8333`
dla komunikacji backendu ze storage na hoście. Dla przeglądarki na tym samym
komputerze ustaw `STORAGE_PUBLIC_ENDPOINT=http://localhost:8333`.
Ten drugi adres jest używany do generowania podpisanych URL zdjęć i nie wymaga
udostępniania storage w sieci LAN. Bez niego podpisy używają `STORAGE_ENDPOINT`.
Bucket i klucze muszą odpowiadać konfiguracji SeaweedFS.

Szczegóły konfiguracji i ograniczeń Receipt opisuje
[README modułu](../../backend/ZPI4M.Backend/src/modules/ZPI4M.Receipt/README.md).
Obraz budowany jest z katalogu backendu, z pominięciem `.env`, lokalnych wyników
kompilacji i testów. Sekrety przekazuj wyłącznie przy uruchomieniu.
