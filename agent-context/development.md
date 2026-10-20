# Praca w repozytorium

Czytaj przed uruchamianiem backendu, zmianami modeli, testami lub pracą nad CI.
Polecenia poniżej są zweryfikowane względem konfiguracji repozytorium; nie
oznacza to potwierdzenia ich wykonania w każdej sesji. Bieżące ograniczenia
opisuje [status](implementation-status.md).

## Wymagania

Backend wymaga SDK .NET 10; baza lokalna Docker i Docker Compose. Narzędzia
hooków wymagają Git, uv i Python 3.10+ zgodnego z projektem `tools`.
Migracje wymagają narzędzia `dotnet-ef` zgodnego z EF Core 10.
Restore i instalacja narzędzi mogą pobierać zależności.

## Lokalna baza i API

W `containers/.env` ustaw własne `POSTGRES_USER` i `POSTGRES_PASSWORD`.
Nie zapisuj rzeczywistych wartości w dokumentacji ani w Git.
Z katalogu `containers/` uruchom:

```bash
docker compose up -d
```

Polecenie tworzy lub uruchamia lokalny kontener i trwały wolumen.
PostgreSQL jest dostępny na `localhost:5434`, baza to `zpi4m_db`.
Skrypty z `database/init/` wykonują się przy inicjalizacji pustego wolumenu,
nie przy każdym restarcie. Nie usuwaj wolumenu w ramach zwykłego uruchomienia.

W `backend/ZPI4M.Backend/src/ZPI4M.Api/.env` ustaw:

```dotenv
DB_LOCAL_CONNECTION_STRING=Host=localhost;Port=5434;Database=zpi4m_db;Username=TWOJ_USER;Password=TWOJE_HASLO
```

Są to placeholdery. Użyj danych zgodnych z lokalną konfiguracją kontenera.
Po przygotowaniu bazy i migracji uruchom z katalogu `ZPI4M.Api`:

```bash
dotnet run --launch-profile http
```

Uruchamianie z tego katalogu zapewnia właściwy kontekst odczytu `.env` przez
`DotNetEnv`. Profil HTTP wskazuje `http://localhost:5283`, profil HTTPS także
`https://localhost:7278`. Host używa przekierowania HTTPS; sprawdź profil
i konfigurację certyfikatu przy diagnozowaniu lokalnych wywołań.
W Development host mapuje dokument OpenAPI. Host wykrywa kontrolery modułu User;
`POST /api/users/register` waliduje dane, zapisuje niepotwierdzone konto razem
ze skrótem tokenu i wysyła wiadomość przez SMTP po zakończeniu zapisu.
`POST /api/users/confirm-email` przyjmuje JSON z polem `token` i zwraca 204
po potwierdzeniu adresu albo 400 dla niepoprawnego, wygasłego lub zużytego tokenu.
Generator tworzy 32 losowe bajty kodowane jako Base64Url (43 znaki); w bazie
przechowujemy skrót SHA-256. Wysyłany e-mail zawiera jawny token i termin
ważności; strony potwierdzenia jeszcze nie ma. Logowanie czeka na implementację.
Stan kodu opisuje [status](implementation-status.md).

## Rejestracja i potwierdzenie adresu

Wszystkie błędy API korzystają z domyślnego formatu `ProblemDetails` ASP.NET Core.
Kontrolery zwracają błędy biznesowe przez `Problem(statusCode: ..., title: ...)`.
Odpowiedź zawiera standardowe pola, np. `type`, `title`, `status` i `traceId`;
`detail` i `instance` mogą wystąpić, jeśli zostały ustawione.
Automatyczna walidacja DTO zwraca standardowy `ValidationProblemDetails`.
Puste błędy HTTP (np. 404, 405 i 429) i nieobsłużone wyjątki korzystają
z domyślnej obsługi `ProblemDetails`, bez dostosowywania pól.
Rejestracja zwraca 201 z `RegisterUserResponse`: `id`, `login`, `name`, `surname`,
`email`, `birthDate`, `isEmailConfirmed` i `createdAt`. Odpowiedź nie zawiera
hasła, skrótu hasła ani tokenów. Sukcesy 202 i 204 nie zawierają body.

1. Wywołaj `POST /api/users/register` z polami `login`, `name`, `surname`,
   `email`, `password`, `birthDate` (data w formacie `YYYY-MM-DD`).
   Sukces zwraca 201, błędne dane 400, zajęty login lub e-mail 409.
2. Odczytaj token z wiadomości i przekaż go w JSON-ie
   `{"token":"TOKEN_Z_WIADOMOSCI"}` do `POST /api/users/confirm-email`.
   Sukces zwraca 204 i ustawia `IsEmailConfirmed = true`. Błędny, wygasły,
   zastąpiony lub już zużyty token zwraca 400.
3. `POST /api/users/resend-confirmation` przyjmuje `{"email":"ADRES_KONTA"}`.
   Dla aktywnego, niepotwierdzonego konta zastępuje poprzedni token i wysyła
   nową wiadomość. Odpowiedź 202 jest również zwracana dla nieznanego adresu,
   potwierdzonego lub nieaktywnego konta i podczas obowiązywania odstępu między
   wysyłkami; nie oznacza w każdym przypadku wysłania nowej wiadomości.

Ponowna wysyłka jest oddzielną operacją od potwierdzenia: `confirm-email`
przyjmuje istniejący token i zużywa go, natomiast `resend-confirmation`
wystawia nowy token i wysyła wiadomość. Ponowne wywołanie `confirm-email`
nie wysyła maila i nie odnawia wygasłego tokenu.

Awaria SMTP zwraca 503. Jeśli nastąpiła podczas rejestracji, konto i token są
już zapisane: należy skorzystać z ponownej wysyłki po wymaganym odstępie,
zamiast ponawiać rejestrację. Anulowanie żądania po zapisie również nie usuwa
konta. Nie ma automatycznych ponowień wysyłki: błąd po zaakceptowaniu wiadomości
przez serwer SMTP nie pozwala zagwarantować, czy wiadomość została dostarczona.
Treść i temat wiadomości znajdują się w `EmailConfirmationMessageSender`.
Token i dane logowania SMTP nie trafiają do odpowiedzi API ani logów aplikacji.

`EmailConfirmationOptions` zawiera domyślne `TokenLifetimeHours = 24`
i `ResendCooldownSeconds = 60`. Host używa tylko
`AddOptions<EmailConfirmationOptions>()`, bez `Bind` i walidacji opcji.
W obecnym kodzie JSON ani zmienne środowiskowe nie nadpisują tych wartości.
Rejestracja i ponowna wysyłka mają wspólny limit 5 żądań/minutę na adres IP;
przekroczenie zwraca 429. Limit IP jest lokalny dla instancji aplikacji.
Odstęp między wysyłkami na konto jest sprawdzany w bazie pod blokadą użytkownika.

## Konfiguracja SMTP

`IEmailSender` z modułu User ma implementację `SmtpEmailSender` opartą na MailKit.
Interfejs, implementacja i `EmailDeliveryException` są razem w `Integrations/Smtp`.
Enum `SmtpSecurityMode` jest zdefiniowany w pliku `SmtpOptions.cs`.
Wysyła wiadomości tekstowe; każda wysyłka tworzy osobne połączenie SMTP.
Projekt User przypina zależność `System.Security.Cryptography.Pkcs` do 10.0.13,
aby użyć poprawionej wersji zależności pobieranej przez MailKit/MimeKit
([advisory Microsoft](https://github.com/advisories/GHSA-846f-f764-76mx)).
`appsettings.json` nie zawiera sekcji SMTP ani EmailConfirmation.
Domyślne ustawienia SMTP znajdują się w `SmtpOptions`; host i adres nadawcy
trzeba dostarczyć przez konfigurację, np. zmienne `SMTP__...` w `.env`.
Konfiguracja jest walidowana przy pierwszej
wysyłce, więc samo uruchomienie API nie wymaga skonfigurowanej skrzynki.

W środowisku procesu lub lokalnym `ZPI4M.Api/.env` ustaw np.:

```dotenv
Smtp__Host=ADRES_SERWERA_SMTP
Smtp__Port=587
Smtp__SecurityMode=StartTls
Smtp__Username=LOGIN_SMTP
Smtp__Password="HASLO_SMTP"
Smtp__SenderEmail=NADAWCA@example.com
Smtp__SenderName=4Money
Smtp__TimeoutSeconds=30
```

To placeholdery; wartości muszą odpowiadać konfiguracji wybranego dostawcy.
`StartTls` wymaga obsługi STARTTLS przez serwer (zwykle port 587).
`SslOnConnect` włącza TLS od początku połączenia (zwykle port 465).
`None` oznacza brak szyfrowania, np. dla lokalnego serwera deweloperskiego.
`SMTP__CHECKCERTIFICATEREVOCATION` domyślnie ma wartość `true`. Jeśli lokalny
system nie może sprawdzić unieważnienia certyfikatu (np. brak dostępu do CRL/OCSP),
można tymczasowo ustawić `false` w lokalnym `.env`. To wyłącza sprawdzanie
unieważnienia, ale pozostawia TLS, sprawdzanie nazwy serwera i zaufania do
certyfikatu. W środowisku produkcyjnym należy zapewnić działające sprawdzanie
unieważnienia i pozostawić `true`.
Login i hasło muszą być podane razem; oba puste wyłączają uwierzytelnianie SMTP.
Zmiana dostawcy polega na zmianie konfiguracji. Plik `.env` jest ignorowany przez
Git; nie zapisuj rzeczywistych danych skrzynki w `appsettings.json`.
`.env` jest ładowany przed utworzeniem konfiguracji hosta, aby te wartości
były dostępne dla mechanizmu Options i DI.

## Modele i migracje

Zmiany wykonuj w modelach i konfiguracjach EF, uwzględniając kontekst modułu
oraz wspólny `AppDbContext`. Z katalogu `backend/ZPI4M.Backend/src/`:

```bash
dotnet ef migrations add NazwaZmiany --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext --output-dir Migrations
```

To polecenie tworzy pliki migracji i aktualizuje snapshot. Przejrzyj wygenerowane
operacje, szczególnie usuwanie danych, zmiany typów i schematów. Dopiero dla
właściwej lokalnej bazy zastosuj istniejące migracje:

```bash
dotnet ef database update --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext
```

Polecenie zmienia bazę. Przy uruchomieniu EF z katalogu `src` upewnij się, że
`DB_LOCAL_CONNECTION_STRING` jest dostępne w środowisku procesu; nie zakładaj,
że plik `.env` w podkatalogu API zostanie odnaleziony z dowolnego katalogu.

Pierwsza migracja jest pusta: projekt zakłada strukturę utworzoną przez SQL
inicjalizacyjny. Nie traktuj migracji jako samodzielnego sposobu zbudowania
całej bazy od zera. Następna migracja przenosi `expense_share` do `expense`.
Kolejne dodają `UserToken` i `IsEmailConfirmed`, usuwają stare pola refresh
tokenu z `User` i zmieniają mapowania dat na `timestamp with time zone`.
Migracja Notifications zakłada istniejącą tabelę z SQL init i zmienia tylko
typ `sent_at`; jej snapshot zawiera już encję Notification.

Nie edytuj SQL init ani zastosowanych migracji w ramach kolejnej zmiany modelu.
Snapshot i plik Designer są generowane wraz z nową migracją i trafiają do Git.
Przy zmianie typu timestamp sprawdź strefę dotychczasowych danych. Sam typ
`DateTime` w C# nie określa typu SQL — decyduje konfiguracja EF.

[Notatki migracyjne](../notes/MIGRATIONS.md) zawierają również procedury
odtwarzania i scaffoldingu. To materiał pomocniczy; ponowne generowanie modeli
z `--force`, kasowanie historii lub modyfikowanie starych migracji nie jest
standardowym krokiem dodania funkcji.

## Kontrole backendu

Poniższe polecenia wykonuj z katalogu głównego repozytorium:

```bash
dotnet restore backend/ZPI4M.Backend/ZPI4M.Backend.slnx
dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --configuration Release --no-restore
dotnet format style backend/ZPI4M.Backend/ZPI4M.Backend.slnx --severity warn --exclude-diagnostics IDE0005 --verify-no-changes --no-restore
dotnet format whitespace backend/ZPI4M.Backend/ZPI4M.Backend.slnx --verify-no-changes --no-restore
```

Restore, build i test zapisują artefakty. `--no-build` zakłada wcześniejszy udany
build tej konfiguracji. Polecenia formatowania z `--verify-no-changes`
sprawdzają styl bez poprawiania źródeł. Ustawienia są w
[EditorConfig](../backend/.editorconfig).

Pomocnik CI uruchamia osobne zestawy na podstawie nazw śledzonych projektów:

```bash
bash tools/ci/backend_checks.sh detect
bash tools/ci/backend_checks.sh unit --results .cache/backend-results
bash tools/ci/backend_checks.sh integration --results .cache/backend-results
```

Użyj świeżego katalogu wyników przy kolejnych uruchomieniach. Filtry wymagają
końcówek `.UnitTests.csproj` lub `.IntegrationTests.csproj`. Obecnie w lokalnym
backendzie nie ma projektów testowych. Komunikat „not configured” nie oznacza
wykonania testów. Zgodnie z ustaleniami użytkownika nie dodawaj nowych testów
ani informacyjnych plików w `notes` przy tych pracach. Jeśli zakres zostanie
zmieniony, nowe projekty testowe uwzględnij w solucji i konwencji CI.
Integracyjne fixture'y muszą zapewniać zależności; workflow backendu nie
uruchamia bazy dla testów.

## Hooki i konwencje Git

Z katalogu głównego, przy potrzebie konfiguracji lokalnych hooków:

```bash
uv sync --project tools --only-group pre-commit --locked
uv run --project tools --only-group pre-commit --locked python tools/hooks/install.py
```

Instalacja zmienia lokalne środowisko i hooki Git; nie jest wymagana do samego
czytania dokumentacji. Instalator wykrywa konflikty istniejących hooków.
Sprawdzenie wszystkich plików:

```bash
uv run --project tools --only-group pre-commit --locked pre-commit run --all-files
```

To polecenie może poprawiać pliki. Przejrzyj zmiany; hooki nie wykonują `git add`.
Hook .NET sprawdza formatowanie po restore, nie wykonuje builda i testów.

Gałąź powiązana z zadaniem używa oryginalnego identyfikatora ClickUp, np.
`CU-869fb6e1e/Autor`. Hook dodaje do commita `CU-869fb6e1e - opis`.
Bez identyfikatora ostrzega i pozostawia wiadomość; nie wymusza Conventional
Commits i nie kontaktuje się z ClickUp. Szczegóły w [instrukcji narzędzi](../tools/README.md).

## Dobór weryfikacji

| Zmiana | Odpowiednia kontrola |
| --- | --- |
| Dokumentacja | Zgodność ze źródłami, linki, ścieżki i Markdown; bez uruchamiania usług |
| Backend / API | Build, kontrola stylu i testy zmienianego zachowania |
| Model danych | Przegląd migracji i snapshotu oraz test na przeznaczonej do tego bazie |
| Hooki | Testy `python3 -m unittest discover -s tools/hooks/tests` i kontrola właściwego hooka |
| Infrastruktura | Kontrole zgodne z [README Proxmox](../proxmox-infra/README.md); uwzględnij, że merge do master uruchamia wdrożenie |

Nie wnioskuj o poprawności na podstawie samego zielonego statusu pipeline'u.
Raportuj wykonane kontrole, pominięte zestawy i ograniczenia. Nie loguj sekretów,
autoryzacyjnych URL-i ani rzeczywistych danych użytkowników.

## Źródła

- [Compose](../containers/docker-compose.yml), [host API](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs) i [profile uruchomienia](../backend/ZPI4M.Backend/src/ZPI4M.Api/Properties/launchSettings.json)
- [Migracje](../notes/MIGRATIONS.md)
- [Narzędzia, hooki i CI](../tools/README.md), [polska instrukcja hooków](../notes/precommit_notes.md)
- [Workflow backendu](../.github/workflows/backend.yml) i [pomocnik CI](../tools/ci/backend_checks.sh)
