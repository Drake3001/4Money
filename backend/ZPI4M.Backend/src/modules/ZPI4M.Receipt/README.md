# Receipt

Moduł obsługuje analizę OCR, przechowywanie zdjęć paragonów i zapis wyników
w PostgreSQL. Punktem wejścia dla pozostałych modułów jest `IReceiptService`.
Paragon jest źródłem danych i załącznikiem; nie jest wydatkiem ani pozycją wydatku.

## Granice i odpowiedzialności

Receipt nie udostępnia kontrolerów MVC ani endpointów API. Docelowo dodawanie
i wyświetlanie paragonu będzie dostępne przez Expense. Ten przepływ nie jest
jeszcze zaimplementowany.

Przed wywołaniem Receipt warstwa aplikacyjna Expense ma sprawdzić aktualny dostęp
użytkownika do wydatku i ustalić przypisany do niego `receiptId`. Serwisy Receipt
nie powtarzają tej kontroli i nie znają zasad członkostwa w grupach. Każda przyszła
ścieżka udostępniania paragonu musi zapewnić taką autoryzację.

| Element | Odpowiedzialność |
| --- | --- |
| `ReceiptService` | Koordynacja OCR, storage i zapisu rekordu |
| `HttpReceiptAnalysisService` | Wywołanie zewnętrznego OCR i walidacja struktury odpowiedzi |
| `S3FileStorageService` | Upload, podpisany URL GET i usuwanie obiektu przez API zgodne z S3 |
| `ReceiptRepository` / `ReceiptDbContext` | Dostęp do tabeli `receipt.receipt` |

Serwis, repozytorium i `ReceiptDbContext` są rejestrowane jako scoped i korzystają
z tego samego kontekstu. `ReceiptService` wywołuje `SaveChangesAsync`. Nie tworzy
wydatków, ich pozycji ani kategorii i nie realizuje korekt OCR przez użytkownika.
`CreateAsync` zapisuje sam paragon; powiązanie z wydatkiem oraz obsługa nieudanego
przypięcia należą do przyszłej integracji z Expense.

## Operacje IReceiptService

| Metoda | Wynik |
| --- | --- |
| `CreateAsync` | Analizuje plik, zapisuje zdjęcie i rekord; zwraca ID paragonu |
| `GetByIdAsync` | Zwraca rekord albo `null`, jeśli nie istnieje |
| `GetFileUrlAsync` | Zwraca `Url` i `ExpiresAt`; brak rekordu oznacza `KeyNotFoundException` |
| `DeleteAsync` | Usuwa rekord, a potem próbuje usunąć zdjęcie; brak rekordu jest sukcesem |

Rekord zawiera `Id`, wymagany i unikalny `StorageKey` oraz `ExtractionResult`
typu `jsonb`. `StorageKey` jest kluczem obiektu, nie adresem URL. Migracje są
utrzymywane w projekcie `ZPI4M.Database`; sposób ich stosowania opisuje
[instrukcja migracji](../../../../../notes/MIGRATIONS.md).

## Tworzenie

Serwis wymaga czytelnego strumienia z niepustą zawartością od bieżącej pozycji
oraz niepustego `contentType`. Nie narzuca limitu rozmiaru, listy formatów ani
walidacji sygnatury pliku. Kopia trafia do tymczasowego pliku usuwanego przy
zamknięciu, również w razie błędu lub anulowania. Strumień wywołującego pozostaje
otwarty; strumienie bez przewijania są obsługiwane.

Kolejność: OCR → upload zdjęcia → zapis paragonu. Obaj dostawcy otrzymują tę samą
zawartość. Wynik OCR jest zapisywany jako surowy JSON w `ExtractionResult`,
bez interpretacji pozycji. Błąd OCR przerywa tworzenie przed uploadem. Pusty klucz
z magazynu powoduje `InvalidOperationException`. Host korzysta z rzeczywistych
klientów OCR i storage; nie przełącza się na atrapy po błędzie.

Po błędzie dodawania lub zapisu rekordu, również przy anulowaniu, zdjęcie
**pozostaje w storage**. Nowa encja jest odłączana od trackera, a pierwotny wyjątek
przekazywany wywołującemu. Pozostałe śledzone encje pozostają nietknięte.
Ostrzeżenie zawiera tylko ID paragonu i typ błędu, bez obiektu wyjątku, treści OCR,
klucza storage i URL.

Wyjątek zapisu nie dowodzi, że baza nie zatwierdziła rekordu: połączenie może
zostać przerwane przed potwierdzeniem. Dlatego serwis nie usuwa wtedy zdjęcia.
Chroni to przed utratą pliku istniejącego paragonu kosztem możliwości pozostawienia
osieroconego zdjęcia. Nieudany upload bez zwróconego klucza również może pozostawić
plik, którego serwis nie potrafi posprzątać. Tworzenie nie jest automatycznie
ponawiane; ponowne wywołanie może utworzyć kolejny paragon.

## Magazyn S3

`S3FileStorageService` wykorzystuje oficjalny AWS SDK i prywatne obiekty w
istniejącym buckecie SeaweedFS. S3 jest tutaj interfejsem komunikacji; użycie SDK
nie oznacza przechowywania plików w chmurze Amazon AWS.
Upload zapisuje zawartość od bieżącej pozycji strumienia, zachowuje strumień
otwarty i zwraca klucz `receipts/{Guid:N}`.
Nieprzewijalny strumień jest kopiowany do usuwanego pliku tymczasowego.
Nie wprowadza limitów rozmiaru ani formatów. MIME pochodzi od wywołującego.

Odczyt tworzy podpisany URL GET (domyślnie 15 minut) bez sprawdzania istnienia
obiektu; `ExpiresAt` jest terminem przekazanym do podpisania. Usuwanie jest
idempotentne dla nieistniejącego obiektu, ale błędy dostępu, brak bucketa i błędy
połączenia są przekazywane wywołującemu. Operacje respektują token anulowania;
SDK podpisuje URL lokalnie, z kontrolą anulowania przed i po podpisaniu.
Brak własnych ponowień ponad mechanizm SDK i brak wspólnej transakcji z bazą.

Konfiguracja jest walidowana przy starcie API. Ustawienia i uruchomienie storage
opisano poniżej. Host rejestruje rzeczywiste klienty OCR i storage, bez endpointów
HTTP modułu Receipt.

Otrzymany podpisany URL pozwala pobrać zdjęcie bezpośrednio ze storage do czasu
wygaśnięcia. Nie należy go logować ani udostępniać osobom bez uprawnień.

## Konfiguracja i uruchomienie

### Który plik .env uzupełnić?

**Dla backendu uzupełnij `backend/ZPI4M.Backend/src/ZPI4M.Api/.env`.** Samo
uzupełnienie pliku w `containers/storage` nie konfiguruje połączenia backendu.

| Plik | Kto go używa | Zawartość |
| --- | --- | --- |
| `backend/ZPI4M.Backend/src/ZPI4M.Api/.env` | Backend .NET | Połączenie z bazą, adresy OCR i storage, klucze klienta S3 |
| `containers/storage/.env` | Docker Compose storage | Port, adres nasłuchiwania, wolumen, bucket i klucze SeaweedFS |
| `containers/.env` | Docker Compose bazy | Dane uruchomienia lokalnego PostgreSQL |

To osobne pliki, które nie synchronizują się automatycznie. Backend nie wczytuje
`.env` z katalogów `containers`. Pliki `env.example` i `.env.example` są wzorami
i również nie są automatycznie wczytywane.

### Ustawienia backendu

Do istniejącego `.env` w `ZPI4M.Api` dodaj brakujące ustawienia na podstawie
[`.env.example`](../../ZPI4M.Api/.env.example). Zachowaj istniejący
`DB_LOCAL_CONNECTION_STRING`; przykład obejmuje ustawienia Receipt, a nie całą
konfigurację hosta. Nie nadpisuj swojego pliku przykładem.

```dotenv
# Przykład lokalny: zastąp klucze wartościami z konfiguracji swojego storage.
STORAGE_ENDPOINT=http://127.0.0.1:8333
STORAGE_BUCKET=receipts
STORAGE_REGION=us-east-1
STORAGE_ACCESS_KEY=replace_with_local_access_key
STORAGE_SECRET_KEY=replace_with_random_local_secret_key
STORAGE_URL_EXPIRATION_MINUTES=15

# Przykład adresu, nie domyślna ani automatycznie uruchamiana usługa OCR.
OCR_BASE_URL=http://127.0.0.1:8000
OCR_TIMEOUT_SECONDS=120
```

| Zmienna | Znaczenie / domyślna wartość |
| --- | --- |
| `OCR_BASE_URL` | Wymagany adres usługi OCR HTTP(S), bez ścieżki, danych logowania, query i fragmentu |
| `OCR_TIMEOUT_SECONDS` | Opcjonalny timeout; domyślnie `120`, zakres `1–4294967` sekund |
| `STORAGE_ENDPOINT` | Wymagany adres serwera S3 HTTP(S), bez ścieżki, danych logowania, query i fragmentu |
| `STORAGE_PUBLIC_ENDPOINT` | Opcjonalny adres HTTP(S) dla przeglądarki, o tych samych ograniczeniach; używany wyłącznie do podpisywania URL. Domyślnie `STORAGE_ENDPOINT` |
| `STORAGE_BUCKET` | Wymagany istniejący bucket |
| `STORAGE_REGION` | Wymagany region używany do podpisywania żądań |
| `STORAGE_ACCESS_KEY` | Wymagany klucz dostępu backendu |
| `STORAGE_SECRET_KEY` | Wymagany sekret backendu |
| `STORAGE_URL_EXPIRATION_MINUTES` | Opcjonalna ważność URL; domyślnie `15`, zakres `1–10080` minut |

Konfiguracja OCR i storage jest walidowana przy starcie. Nie ma wtedy sieciowego
sprawdzania ich dostępności; jest ona potrzebna podczas odpowiednich operacji.
Brak poprawnego `OCR_BASE_URL` blokuje start hosta nawet wtedy, gdy zamierzasz
korzystać tylko ze storage. Ustaw właściwy adres usługi; nie dodano przełącznika
wyłączającego OCR ani automatycznej atrapy.

### Powiązanie ze storage

W `.env` backendu i `.env` kontenera muszą zgadzać się `STORAGE_BUCKET`,
`STORAGE_ACCESS_KEY` oraz `STORAGE_SECRET_KEY`. `STORAGE_REGION` jest wymagany
przez klienta backendu do podpisywania; obecny Compose go nie wykorzystuje.

`STORAGE_ENDPOINT` jest adresem połączenia klienta, a `STORAGE_BIND_IP`
i `STORAGE_PORT` określają publikację portu kontenera. Przy lokalnym storage
z przykładowej konfiguracji endpoint to `http://127.0.0.1:8333`. Przy storage
na serwerze ustaw adres tego serwera. Jeśli backend działa we własnym kontenerze,
`127.0.0.1` oznacza ten kontener, a nie komputer hosta ani SeaweedFS.

Adres podpisanego URL musi być osiągalny z przeglądarki. Gdy adres połączenia
backendu jest inny, ustaw `STORAGE_PUBLIC_ENDPOINT`. Lokalnie w Dockerze można
użyć `STORAGE_ENDPOINT=http://host.docker.internal:8333` i
`STORAGE_PUBLIC_ENDPOINT=http://localhost:8333`, jeśli przeglądarka działa na
hoście Dockera. Podpis jest generowany od razu dla publicznego adresu, bez zmiany
hosta po podpisaniu; upload i usuwanie nadal używają `STORAGE_ENDPOINT`.
Adres loopback działa tylko na tym samym komputerze. Samo wygenerowanie URL
nie potwierdza połączenia ze storage, ponieważ podpisywanie odbywa się lokalnie.

### Lokalne uruchomienie

Jeżeli storage nie jest już udostępniony na serwerze, przygotuj jego lokalną
konfigurację na podstawie [env.example](../../../../../containers/storage/env.example).
Z katalogu głównego repozytorium, tylko gdy plik jeszcze nie istnieje:

```powershell
if (!(Test-Path containers/storage/.env)) {
    Copy-Item containers/storage/env.example containers/storage/.env
}
```

Przed startem zastąp przykładowe klucze własnymi wartościami i przepisz je do
`.env` backendu. Przykład wybiera `STORAGE_BIND_IP=127.0.0.1`; nie pomijaj tej
zmiennej lokalnie, ponieważ Compose ma inny domyślny adres dla środowiska VM.
Uruchom Docker Desktop z silnikiem Linux, a następnie z katalogu repozytorium:

```powershell
docker compose --env-file containers/storage/.env -f containers/storage/docker-compose.yml config --quiet
docker compose --env-file containers/storage/.env -f containers/storage/docker-compose.yml up -d
docker compose --env-file containers/storage/.env -f containers/storage/docker-compose.yml ps
```

[Compose storage](../../../../../containers/storage/docker-compose.yml) uruchamia
SeaweedFS 4.48. Bucket przygotowuje `weed mini`; backend go nie tworzy. Dane są
w nazwanym wolumenie, domyślnie `zpi4m_storage_data`. Zwykłe `docker compose down`
zachowuje wolumen; opcja `-v` usuwa dane. Zmiana nazwy bucketa nie migruje plików.

Backend wymaga też skonfigurowanej bazy z zastosowanymi migracjami; przygotowanie
opisuje instrukcja migracji podlinkowana wcześniej. Po uzupełnieniu `.env`
uruchom API z jego katalogu (poniższe polecenie zaczyna się w katalogu repozytorium):

```powershell
Set-Location backend/ZPI4M.Backend/src/ZPI4M.Api
dotnet run
```

`Env.Load()` działa przed utworzeniem konfiguracji hosta i szuka `.env` w bieżącym
katalogu. Po zmianie ustawień uruchom backend ponownie. Działające OCR jest
potrzebne do `CreateAsync`; odczyt URL nie wywołuje OCR.

Rzeczywistych `.env`, sekretów i danych finansowych nie dodajemy do repozytorium.

## Analiza OCR

`HttpReceiptAnalysisService` implementuje `IReceiptAnalysisService` przez klienta
typowanego `IHttpClientFactory`. Usługa OCR jest zewnętrzna, bez autoryzacji.
Host zawsze używa klienta HTTP; nie przełącza się na atrapę po błędzie.

Przykładowy `OCR_BASE_URL` to `http://127.0.0.1:8000`; kod nie ma domyślnego
adresu usługi. Zmienne i zakres timeoutu opisano w sekcji konfiguracji.

Klient wywołuje zewnętrzne API OCR, nie endpoint udostępniany przez Receipt.
Wysyła jeden plik przez `POST /api/v1/extract`, jako `multipart/form-data`
z polem `files`, nazwą `receipt.bin` i MIME wywołującego. Wysyła bajty od bieżącej
pozycji bez przewijania i pozostawia strumień otwarty również po błędzie.
Timeout obejmuje wysyłanie i odczyt odpowiedzi. Brak automatycznych ponowień,
przekierowań, cookies oraz wywołań endpointów health/debug.

Odpowiedź musi mieć status `200` i być obiektem JSON z `receipt` typu obiekt,
`warnings` typu tablica i `needs_review` typu boolean. Wnętrze tych struktur nie
jest walidowane ani interpretowane. `needs_review: true`, ostrzeżenia i wartości
`null` nie blokują zapisu. Cała odpowiedź, włącznie z dodatkowymi polami, trafia
do `ExtractionResult` (`jsonb`). Wcześniejsze rekordy mogą zawierać płaski format
atrapy; nie są przepisywane. Nie powstają wydatki ani powiązania kategorii.
Poprawność kwot, produktów i sum wymaga osobnej walidacji przed wykorzystaniem
tych danych do utworzenia wydatku.

`ReceiptAnalysisException.Kind` rozróżnia `HttpError` (z `StatusCode`),
`TransportError`, `Timeout` i `InvalidResponse`. Anulowanie wywołującego pozostaje
`OperationCanceledException`. Komunikaty błędów są stałe, bez treści paragonu,
body odpowiedzi i wewnętrznych wyjątków transportu. Klient nie loguje payloadu.
Każdy błąd OCR przerywa tworzenie przed uploadem i zapisem rekordu.

## Odczyt i usuwanie

`GetByIdAsync` zwraca `null` dla nieistniejącego ID. `GetFileUrlAsync` zgłasza
wtedy `KeyNotFoundException`; dla istniejącego rekordu zwraca wynik magazynu.
`DeleteAsync` jest idempotentne dla brakującego rekordu.

Usuwanie najpierw zatwierdza usunięcie rekordu w bazie, a następnie usuwa plik.
Błąd bazy pozostawia zdjęcie i przywraca poprzedni stan śledzenia encji.
Powiązanie z wydatkiem może zablokować usunięcie przez istniejący FK PostgreSQL;
serwis nie odłącza wydatków. Błąd usunięcia pliku po zapisie jest logowany,
a metoda kończy się sukcesem. Ponowne `DeleteAsync` nie posprząta takiego pliku,
jeśli rekord paragonu został już usunięty.

Sprzątanie pliku ma własny token z timeoutem 10 sekund, niezależny od anulowania
żądania; dostawca magazynu musi respektować ten token. Timeout korzysta ze
wstrzykiwanego `TimeProvider`: w aplikacji jest to zegar systemowy, a w testach
zegar kontrolowany. Log zawiera wyłącznie ID
paragonu i typ błędu, bez treści OCR, zdjęcia, klucza i URL.

## Ograniczenia

Brak wspólnej transakcji bazy i magazynu, automatycznych ponowień oraz trwałej
kolejki sprzątania oznacza możliwość pozostawienia osieroconego zdjęcia.
Niejednoznaczny wynik zatwierdzenia zapisu (np. utrata połączenia w trakcie commit)
nie powoduje usuwania zdjęcia. Nie ma gwarancji idempotencji tworzenia ani
automatycznej weryfikacji, czy zapis został zatwierdzony.
Kopie tymczasowe wymagają miejsca na dysku, a JSON odpowiedzi OCR jest wczytywany
do pamięci. Brak limitu rozmiaru pliku i odpowiedzi jest ograniczeniem tej wersji.

## Testy

Wszystkie polecenia w tej sekcji uruchamiaj z katalogu głównego repozytorium.

| Zestaw | Wymagania | Co sprawdza |
| --- | --- | --- |
| `ZPI4M.Receipt.UnitTests` | .NET 10, bez `.env` i usług zewnętrznych | Trzy serwisy Receipt i konfigurację |
| `TestCleanupTests` w projekcie integracyjnym | .NET 10, bez `.env` i usług zewnętrznych | Zachowanie pierwotnego błędu i błędu sprzątania |
| `S3FileStorageIntegrationTests` | Osiągalny SeaweedFS, ustawienia storage, flaga `STORAGE_INTEGRATION_TESTS=1` | Rzeczywisty upload, pobranie zdjęcia i usuwanie |

### Testy bez kontenerów i płatnego OCR

Testy jednostkowe korzystają z xUnit, EF Core InMemory, atrap dostawców
i kontrolowanego transportu HTTP. Handler testowy zastępuje transport sieciowy
klienta OCR; odpowiedzi, błędy i opóźnienia są symulowane. Testy konfiguracji
tworzą klienta, ale nie wysyłają żądań. Żaden obecny test nie wywołuje prawdziwego
OCR, również po skonfigurowaniu jego rzeczywistego adresu w `.env` aplikacji.

Zapis do bazy odbywa się w pamięci, a operacje S3 są zastępowane lub przechwytywane.
Testy mogą tworzyć pliki tymczasowe na lokalnym dysku, żeby sprawdzić kopiowanie
i sprzątanie; nie zapisują danych do rzeczywistego PostgreSQL ani SeaweedFS.
InMemory nie weryfikuje FK, unikalności klucza ani PostgreSQL `jsonb`.

```powershell
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Receipt.UnitTests/ZPI4M.Receipt.UnitTests.csproj --configuration Release
```

Testy jednostkowe obejmują multipart OCR, konfigurację, podpisywanie żądań S3,
strumienie bez przewijania, dane binarne, HTTPS, przerwane kopiowanie i wysyłanie,
timeout, anulowanie i obsługę błędów. Sprawdzają też
zapis pełnej odpowiedzi OCR, zgodność bajtów OCR/storage i brak uploadu po błędzie
analizy. Test SDK z przechwyconym transportem HTTP nie wymaga serwera storage.

Timeout sprzątania jest sprawdzany przez przesunięcie `FakeTimeProvider` o 10
sekund, bez rzeczywistego czekania. Regresja wyjątku po zapisie używa interceptora
i odczytu z nowego kontekstu, a nie awarii rzeczywistego PostgreSQL.

Cztery testy pomocnika sprzątania można uruchomić niezależnie od flagi storage:

```powershell
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Receipt.IntegrationTests/ZPI4M.Receipt.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~TestCleanupTests
```

### Rzeczywisty test storage — uruchamiany świadomie

Ten test wysyła syntetyczny PNG do storage. Sprawdza podpisany GET, zgodność
bajtów i MIME, odmowę anonimowego GET, usunięcie i ponowne usunięcie. Nie wywołuje
OCR ani PostgreSQL. Sprząta tylko własny obiekt, bez usuwania bucketa i wolumenu.
Przy błędzie operacji i sprzątania zachowuje oba wyjątki.

Test sam wczytuje `STORAGE_*` z `backend/ZPI4M.Backend/src/ZPI4M.Api/.env`;
ustawione zmienne środowiskowe mają pierwszeństwo. Nie wymaga `OCR_BASE_URL`
ani `DB_LOCAL_CONNECTION_STRING`, ponieważ nie uruchamia hosta API.
Flaga włączająca jest zmienną środowiskową procesu PowerShell, nie wpisem
w `.env`. Po uruchomieniu storage i sprawdzeniu adresu docelowego:

```powershell
$previousStorageTestFlag = $env:STORAGE_INTEGRATION_TESTS
try {
    $env:STORAGE_INTEGRATION_TESTS = '1'
    dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Receipt.IntegrationTests/ZPI4M.Receipt.IntegrationTests.csproj --configuration Release
}
finally {
    $env:STORAGE_INTEGRATION_TESTS = $previousStorageTestFlag
}
```

Bez flagi `1` test storage jest jawnie pomijany, także w zwykłym CI. Cztery testy
pomocnika nadal działają. Przy włączonej fladze brak ustawień lub niedostępność
storage oznacza błąd testu; kontener może działać lokalnie albo na serwerze.

### Stan weryfikacji

Ostatnia wykonana weryfikacja kodu, **2026-10-27**: 125 testów jednostkowych
i 4 testy pomocnika sprzątania przeszły. Rzeczywisty test SeaweedFS był jawnie
pominięty. Kompilacja Release solucji przeszła bez ostrzeżeń i błędów; kontrole
stylu i whitespace w trybie bez zmian również zakończyły się powodzeniem.

Nie jest to potwierdzenie działania rzeczywistego OCR, ograniczeń PostgreSQL
ani pełnego przepływu OCR → storage → PostgreSQL. Te integracje wymagają osobnej
weryfikacji. Przy zmianie samej dokumentacji nie uruchamiano ponownie testów kodu.
