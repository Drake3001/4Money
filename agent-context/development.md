# Praca w repozytorium

Czytaj przed uruchamianiem backendu, zmianami modeli, testami lub pracą nad CI.
Polecenia poniżej są zweryfikowane względem konfiguracji repozytorium; nie
oznacza to potwierdzenia ich wykonania w każdej sesji. Bieżące ograniczenia
opisuje [status](implementation-status.md).

Rejestracje hosta, łańcuch migracji, konfigurację i polecenia ponownie sprawdzono
statycznie 2026-10-29; bez uruchamiania aplikacji, testów ani migracji.
Solucja zawiera po trzy projekty testowe User, Banking i Notifications
oraz dwa Receipt, dwa Expense, Grouping i Shared — łącznie piętnaście. Starsze wyniki uruchomień opisuje status; nie traktuj
ich jako nowej weryfikacji obecnej rewizji po rebase.

## Wymagania

Backend wymaga SDK .NET 10; baza lokalna Docker i Docker Compose. Narzędzia
hooków wymagają Git, uv i Python 3.10+ zgodnego z projektem `tools`.
Migracje używają lokalnego `dotnet-ef` 10.0.4 z głównego `dotnet-tools.json`.
Po clone lub zmianie manifestu wykonaj `dotnet tool restore`. Wrapper
`tools/ef.cs` wymaga SDK .NET 10; Python nie jest potrzebny do migracji.
Restore i instalacja narzędzi mogą pobierać zależności.

ProjGraph.Cli 1.2.0 jest przypięty w głównym `dotnet-tools.json` jako lokalne
narzędzie deweloperskie .NET, poza zależnościami aplikacji. Instalacja po clone:
`dotnet tool restore` z katalogu głównego. Polecenia diagramów projektów, klas
i ERD opisuje [instrukcja narzędzi](../tools/README.md#architecture-diagrams-projgraph).
Generowanie nie wymaga uruchomionej bazy ani builda backendu.

## Lokalna baza i API

Docker uruchamia pięć usług przez jeden `containers/local/docker-compose.yml`
i jeden `containers/local/.env`: frontend Vite, PostgreSQL, backend, SeaweedFS i OCR. Sam
frontend można uruchomić niezależnie przez `docker compose up --build -d frontend`
z katalogu `containers/local/`; nie wymaga instalacji Node na hoście. Pełna
konfiguracja, porty i przejście ze starych Compose są w
[instrukcji kontenerów](../containers/local/README.md). Pliki `.env` w katalogach
API i workera są potrzebne tylko do uruchamiania tych aplikacji bez Dockera.

W `containers/local/.env` ustaw własne `POSTGRES_USER` i `POSTGRES_PASSWORD`.
Szablon znajduje się w `containers/local/.env.example`. Skopiuj go do `.env`, jeśli
plik lokalny jeszcze nie istnieje. Istniejący wolumen PostgreSQL zachowuje
pierwotne dane logowania — zmiana `.env` ich nie zmienia.
Nie zapisuj rzeczywistych wartości w dokumentacji ani w Git.
Z katalogu `containers/local/` uruchom:

```bash
docker compose up -d postgres-db
```

Równoważne uruchomienie z katalogu głównego repo z jawnym plikiem środowiska:

```bash
docker compose --env-file containers/local/.env -f containers/local/docker-compose.yml up -d postgres-db
```

Ostrzeżenia o brakujących POSTGRES_USER/POSTGRES_PASSWORD oznaczają, że Compose
nie dostał tych wartości. Uzupełnij containers/local/.env przed inicjalizacją nowego
wolumenu; nie zakładaj domyślnego loginu PostgreSQL. Poświadczenia bazy są inne
niż loginy i hasło kont aplikacji z seeda.

Polecenie tworzy lub uruchamia lokalny kontener i trwały wolumen.
PostgreSQL jest dostępny na `localhost:5434`, baza to `zpi4m_db`.
Skrypty z `database/init/` wykonują się przy inicjalizacji pustego wolumenu,
nie przy każdym restarcie. Nie usuwaj wolumenu w ramach zwykłego uruchomienia.

Kolejność przygotowania pustej bazy: SQL init (automatycznie z Compose na nowym
wolumenie) → wszystkie migracje EF → `database/seed_local.sql`. Migracje tworzą
m.in. notification_recipient, nowe kolumny powiadomień oraz unikalne
`receipt.receipt.storage_key`, więc seed wykonany
przed migracjami nie zadziała. Samo `database update` nie odtwarza pustej bazy
bez baseline SQL. Przy zwykłym starcie istniejącej bazy nie uruchamiaj resetu.

W `backend/ZPI4M.Backend/src/ZPI4M.Api/.env` ustaw:

```dotenv
DB_LOCAL_CONNECTION_STRING=Host=localhost;Port=5434;Database=zpi4m_db;Username=TWOJ_USER;Password=TWOJE_HASLO
```

Są to placeholdery. Użyj danych zgodnych z lokalną konfiguracją kontenera.
Szablon API to `backend/ZPI4M.Backend/src/ZPI4M.Api/.env.example`; zawiera też
`UserAuthentication__SigningKey`, wymagany przy starcie hosta, oraz pola Tink.
Klucz JWT powinien być losowy i mieć co najmniej 32 znaki. Lokalne pliki `.env`
są ignorowane przez Git; przechowuj je z uprawnieniami `600`. Prawdziwe sekrety
Tink wpisuj wyłącznie lokalnie jako `Tink__ClientSecret`; nie umieszczaj ich
w `appsettings.json` ani szablonach. Żądania Banking w Postmanie potrzebują
JWT User, nie sekretu aplikacji Tink. Integracja Tink pozostaje wyłączona przez
`Tink__ContinuousAccessVerified=false` na etapie przygotowania bazy.
Szablon API zawiera poświadczenia i klucz dowodu OCR, a od zmian User także
opcjonalną konfigurację Google oraz prefiksów storage. Domyślne adresy OCR
i storage, bucket, region, timeouty, callback Tink oraz niesekretne ustawienia
SMTP są w `appsettings.json`. SMTP ma domyślny host i tryb połączenia;
adres nadawcy i poświadczenia uzupełnij lokalnie oraz dopasuj serwer przed wysyłką.
Callback Tink musi odpowiadać rejestracji w Tink
i uruchomionemu profilowi (domyślny w JSON zachowuje HTTPS na 7278).
Klucze OCR i storage w JSON pozostają płaskie (`OCR_BASE_URL`, `STORAGE_ENDPOINT`
itd.), zgodnie z istniejącym odczytem przez IConfiguration. Zmienne środowiskowe
mogą nadpisywać wszystkie te ustawienia; istniejący lokalny `.env` zachowuje
pierwszeństwo i nie jest automatycznie przepisywany.
Po przygotowaniu bazy i migracji możesz uruchomić cały stos przez
`docker compose --env-file containers/local/.env -f containers/local/docker-compose.yml up --build -d`
z głównego katalogu repo. Przy uruchamianiu API bez Dockera, z katalogu `ZPI4M.Api`:

```bash
dotnet run --launch-profile http
```

Samo `dotnet run` wybiera pierwszy profil z `Properties/launchSettings.json`,
czyli obecnie `http`, więc daje ten sam efekt co powyższa komenda.
`dotnet run --launch-profile https` uruchamia HTTPS na 7278 i HTTP na 5283.
Oba profile ustawiają `ASPNETCORE_ENVIRONMENT=Development`.
`dotnet run --no-launch-profile` pomija ustawienia launchSettings; adresy
i środowisko wynikają wtedy z pozostałej konfiguracji (domyślnie Production,
jeśli nie ustawiono środowiska). Wywołanie `Env.Load()` nadal jest wykonywane.

W VS Code z rozszerzeniem C# Dev Kit można wybrać w panelu Run and Debug
profil `4Money API (HTTP)` i nacisnąć F5. Konfiguracja w `.vscode/launch.json`
ustawia katalog roboczy API, aby odczytać jego lokalny `.env`, i korzysta
z profilu `http`. PostgreSQL i SeaweedFS należy wcześniej uruchomić osobno:

```powershell
docker compose --env-file containers/local/.env -f containers/local/docker-compose.yml up -d postgres-db seaweedfs
```

Polecenie wykonuj z głównego katalogu repo. Migracje i opcjonalny seed wykonuje
się osobno, zgodnie z opisaną wyżej kolejnością. Worker OCR jest potrzebny
do analizy paragonów; nie jest wymagany do testowania logowania i inboxa.

Uruchamianie z tego katalogu zapewnia właściwy kontekst odczytu `.env` przez
`DotNetEnv`. Profil HTTP wskazuje `http://localhost:5283`, profil HTTPS także
`https://localhost:7278`. Host używa przekierowania HTTPS; sprawdź profil
i konfigurację certyfikatu przy diagnozowaniu lokalnych wywołań.
W Development host mapuje dokument `/openapi/v1.json` (bez wbudowanego Swagger UI).
Host udostępnia kontrolery User, Banking, Notifications, Grouping i 10 operacji
Expense: kategorie, dostępne transakcje, historię, szczegóły, preview tworzenia
i edycji, POST, PUT, DELETE oraz analizę i odczyt paragonu.
Runtime używa rzeczywistych członkostw i metadanych Grouping
we wszystkich środowiskach; usunięto flagę Expense:UseGroupingMock.
Tworzenie, edycja i usuwanie aktualizują TotalPaid/TotalShare w transakcji
koordynatora razem z Expense. AddExpenseServices rejestruje tylko usługi Expense;
host rejestruje implementacje Grouping i User w sekcjach tych modułów.
Docelowy kontrakt i przykłady opisuje [Expense API](../notes/EXPENSE_API.md).
`POST /api/users/register` waliduje dane, zapisuje niepotwierdzone konto razem
ze skrótem tokenu w otwartej transakcji. Zatwierdza ją dopiero po przyjęciu
wiadomości przez SMTP; błąd wysyłki wycofuje konto i token.
`POST /api/users/confirm-email` przyjmuje JSON z polem `token` i zwraca 204
po potwierdzeniu adresu albo 400 dla niepoprawnego, wygasłego lub zużytego tokenu.
Generator tworzy 32 losowe bajty kodowane jako Base64Url (43 znaki); w bazie
przechowujemy skrót SHA-256. E-mail HTML zawiera przycisk i termin ważności.
Token jest we fragmencie linku (`#token=...`), a frontend wywołuje odpowiedni POST.
Po potwierdzeniu adresu pokazuje logowanie z komunikatem sukcesu.
Stan kodu opisuje [status](implementation-status.md).

## Rejestracja i potwierdzenie adresu

Błędy API modułu User korzystają z domyślnego formatu `ProblemDetails` ASP.NET Core.
Kontrolery zwracają błędy biznesowe przez `Problem(statusCode: ..., title: ...)`.
Odpowiedź zawiera standardowe pola, np. `type`, `title`, `status` i `traceId`;
`detail` i `instance` mogą wystąpić, jeśli zostały ustawione.
Automatyczna walidacja DTO zwraca standardowy `ValidationProblemDetails`.
Google i blokada zmiany e-maila konta Google dodają pole `code`; szczegóły
opisuje [kontrakt Google](#rejestracja-i-logowanie-google).
Puste błędy HTTP (np. 404, 405 i 429) i nieobsłużone wyjątki korzystają
z domyślnej obsługi `ProblemDetails`, bez dostosowywania pól.
Rejestracja zwraca 201 z `RegisterUserResponse`: `id`, `login`, `name`, `surname`,
`email`, `birthDate`, `isEmailConfirmed` i `createdAt`. Odpowiedź nie zawiera
hasła, skrótu hasła ani tokenów. Sukcesy 202 i 204 nie zawierają body.
Zapis nowego konta tworzy też zwykłą grupę „Wydatki osobiste” w PLN z jednym
członkiem będącym administratorem. Robi to trigger PostgreSQL w transakcji
rejestracji, przed potwierdzeniem e-maila. Kontrakt odpowiedzi nie zawiera grupy;
po potwierdzeniu i logowaniu jest dostępna przez zwykłe API Grouping.
Błąd zapisu grupy lub członkostwa wycofuje rejestrację. W zwykłej rejestracji
awaria SMTP przed commitem wycofuje konto, token, grupę i członkostwo.
Rejestracja Google ma osobny kontrakt opisany w sekcji Google poniżej.

1. Wywołaj `POST /api/users/register` z polami `login`, `name`, `surname`,
   `email`, `password`, `birthDate` (data w formacie `YYYY-MM-DD`).
   Login ma 3–100 znaków i nie może zawierać `@` (również przy edycji profilu).
   Sukces zwraca 201, błędne dane 400, zajęty login lub e-mail aktywnego konta 409.
   E-mail konta nieaktywnego jest dostępny; jego login pozostaje zajęty.
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

Awaria SMTP zwraca 503 i wycofuje zapis konta oraz tokenu; można ponowić
rejestrację tym samym adresem. Anulowanie przed commitem także wycofuje zapis.
Przy ponownej wysyłce awaria zachowuje poprzedni token i jego daty.
Nie ma automatycznych ponowień. SMTP potwierdza przyjęcie wiadomości,
nie jej dostarczenie do skrzynki. Zobacz ograniczenia transakcji w architekturze.
Treść i temat wiadomości tworzy `UserEmailMessages.EmailConfirmation` w
`Integrations/Smtp`. Serwisy wywołują wspólny `IEmailSender.SendAsync`.
Token i dane logowania SMTP nie trafiają do odpowiedzi API ani logów aplikacji.

`EmailConfirmationOptions` zawiera domyślne `TokenLifetimeHours = 24`
i `ResendCooldownSeconds = 60`. Host używa tylko
`AddOptions<EmailConfirmationOptions>()`, bez `Bind` i walidacji opcji.
W obecnym kodzie JSON ani zmienne środowiskowe nie nadpisują tych wartości.
Rejestracja i ponowna wysyłka mają wspólny limit 5 żądań/minutę na adres IP;
przekroczenie zwraca 429. Limit IP jest lokalny dla instancji aplikacji.
Odstęp między wysyłkami na konto jest sprawdzany w bazie pod blokadą użytkownika.

## Logowanie i sesja

Do lokalnych testów bez rejestracji i SMTP służy `database/seed_local.sql`,
wykonywany po migracjach. Resetuje dane aplikacji i odtwarza pięć kont ze wspólnym
hasłem testowym `TestPassword1!`, opisanych w komentarzach SQL. `seed_dawid`
pozostaje nieaktywny, pozostałe konta są aktywne i potwierdzone. Dla Banking
używaj `postman.banking`, ID `10000000-0000-0000-0000-000000000010`.
Reset usuwa także sesje, połączenia bankowe i TinkUserId. Po resecie zaloguj się
ponownie, a powiązanie Tink odtwórz komendą `link-db` z istniejącego manifestu.
Konfigurację opisuje [instrukcja Postmana](../postman/README.md).

Host wymaga `UserAuthentication__SigningKey` w środowisku lub lokalnym `.env`.
Klucz musi mieć co najmniej 32 znaki i pochodzić z generatora kryptograficznego
(np. 32 losowe bajty zakodowane Base64). Nie zapisuj go w Git. Brak lub błędna
konfiguracja zatrzymuje uruchomienie hosta przez walidację Options.
Opcjonalne ustawienia, również przez `.env`:

```dotenv
UserAuthentication__Issuer=4Money
UserAuthentication__Audience=4Money.Client
UserAuthentication__AccessTokenLifetimeMinutes=15
UserAuthentication__RefreshTokenLifetimeDays=7
```

- `POST /api/users/login`: JSON `{"login":"LOGIN","password":"HASLO"}`.
  Pole `login` przyjmuje login lub aktualny e-mail (3–254 znaki), np.
  `{"login":"anna@example.test","password":"HASLO"}`. Wartość jest przycinana
  i zamieniana na małe litery tak jak przy rejestracji. Wartość zawierająca `@`
  jest wyszukiwana wyłącznie po e-mailu, a pozostałe wartości wyłącznie po loginie.
  Po zablokowaniu rekordu serwis
  ponownie sprawdza zgodność identyfikatora z aktualnym loginem lub e-mailem.
  Wymagane aktywne konto z potwierdzonym e-mailem. Nieznany login/e-mail, błędne hasło
  i niepotwierdzone konto zwracają jednakowe 401. E-mail jest wyszukiwany tylko
  wśród aktywnych kont; adres występujący wyłącznie na nieaktywnych kontach daje 401.
  Dezaktywowane konto po podaniu loginu i poprawnego hasła zwraca 403
  z tytułem `Account is deactivated.`; błędne hasło
  nadal daje ogólne 401. Sukces aktualizuje LastLoginTime
  i w razie potrzeby odświeża hash hasła przez IPasswordHasher.
- `POST /api/users/refresh`: JSON `{"refreshToken":"TOKEN"}`. Każde udane
  odświeżenie wymienia refresh token; poprzedni zwraca 401. Termin sesji jest
  bezwzględny (domyślnie 7 dni od logowania), bez przedłużania przy odświeżeniu.
- Obie operacje zwracają 200 z `accessToken`, `accessTokenExpiresAt`,
  `refreshToken`, `refreshTokenExpiresAt`, `tokenType` równym `Bearer`.
  Daty są UTC. Odpowiedzi mają Cache-Control: no-store.
- `POST /api/users/logout`: nagłówek `Authorization: Bearer ACCESS_TOKEN`,
  bez body. Usuwa bieżącą sesję i zwraca 204; brak ważnego JWT zwraca 401.
  Z wygasłym JWT można najpierw odświeżyć tokeny, a potem się wylogować.

Login, refresh i reset-password mają osobne limity na IP, lokalne dla instancji:
`user-login` — 30/min, `user-refresh` — 120/min, `user-password-reset` — 10/min.
Wyczerpanie jednej polityki nie blokuje pozostałych. Okna są stałe, po 60 sekund,
bez kolejki; każde dopuszczone żądanie zużywa limit, również przy błędnym DTO
lub haśle. Niepoprawne DTO zwracają 400, przekroczenie limitu 429.
Osoby za wspólnym IP współdzielą limit danej operacji. Kluczem jest
Connection.RemoteIpAddress (lub `unknown` przy braku adresu), bez samodzielnego
odczytu nagłówków proxy. Limity nie są współdzielone między instancjami.

Progi można nadpisać w sekcji UserRateLimiting lub zmiennymi środowiskowymi:

```dotenv
UserRateLimiting__LoginPermitLimit=30
UserRateLimiting__RefreshPermitLimit=120
UserRateLimiting__PasswordResetPermitLimit=10
```

Wszystkie progi muszą być dodatnimi liczbami całkowitymi; niepoprawna konfiguracja
zatrzymuje start aplikacji. Rejestracja polityk jest współdzielona przez host API
i testy HTTP przez AddUserRateLimiting. Polityka user-email pozostaje wspólna,
z limitem 5/min/IP.
Wywołania można wykonywać ręcznie w Postmanie.

Kolekcja [4Money.User](../postman/collections/4Money.User.postman_collection.json) zawiera
21 żądań obejmujących obecne API User, w tym operacje Google i zdjęcia profilu. Korzysta ze wspólnego środowiska
`postman/environments/4Money.Local.example.postman_environment.json`.
Login zapisuje tokeny, a tokeny z maili wklejamy ręcznie. Każde żądanie
zatrzymuje Runner; nie jest to automatyczny test całego przepływu.
Zmienne i kolejność opisuje [README Postmana](../postman/README.md).

W bazie pozostaje tylko hash refresh tokenu (Purpose = RefreshToken).
Jedna sesja na konto wynika z UNIQUE (FkUserId, Purpose). Nowe logowanie
zastępuje sesję i unieważnia oba poprzednie tokeny. Refresh zachowuje ID sesji,
więc wcześniejsze access tokeny tej samej sesji działają do swojego wygaśnięcia.
JWT zawiera `sub` (ID użytkownika), `sid` (ID sesji) i `jti`; walidujemy podpis
HS256, issuer, audience, czas bez tolerancji oraz obecność aktualnej,
niewygasłej sesji aktywnego konta z potwierdzonym e-mailem w bazie przy każdym
uwierzytelnieniu. Wylogowanie unieważnia również niewygasłe JWT dla kolejnych
żądań. Reset hasła usuwa refresh token i unieważnia również dotychczasowe JWT.

Login, refresh i logout blokują najpierw rekord użytkownika, potem operują na
tokenie, w jednej transakcji. Warunkowe zużycie refresh tokenu dopuszcza tylko
jedno udane użycie, także dla równoległych żądań. Login, refresh i logout
korzystają z istniejącej tabeli UserToken, bez osobnej migracji uwierzytelniania.
Dezaktywacja wymaga migracji opisanej w swojej sekcji.
Zmiana hasła po podaniu aktualnego hasła pozostaje do implementacji.
Odczyt i edycję profilu, zmianę e-maila, reset hasła i dezaktywację opisano poniżej.

## Rejestracja i logowanie Google

Backend przyjmuje ID token Google Identity Services, a następnie wydaje własne
JWT i refresh tokeny. Nie przyjmuje access tokenu Google ani kodu OAuth.
Frontend z przyciskiem Google i formularzem danych nie jest jeszcze podłączony.
Google nie zastępuje lokalnej obsługi sesji, haseł i poczty.

Integracja jest domyślnie wyłączona. W `containers/local/.env` dla Compose lub
`src/ZPI4M.Api/.env` przy uruchamianiu bez Dockera ustaw:

```dotenv
GOOGLEAUTHENTICATION__ENABLED=true
GOOGLEAUTHENTICATION__CLIENTID=REPLACE_WITH_WEB_CLIENT_ID.apps.googleusercontent.com
```

Client ID jest publicznym identyfikatorem klienta OAuth typu Web application,
nie client secret. Frontend musi użyć tego samego ID. W Google Cloud ustaw
dozwolone JavaScript origins odpowiadające faktycznym adresom frontendu
(schemat, host, port; bez ścieżki). Dla lokalnego i wdrożonego środowiska można
używać osobnych klientów. Ten przepływ nie wymaga client secret ani callbacku API.
Walidacja opcji blokuje start przy włączonej integracji bez poprawnego ClientId.

1. `POST /api/users/google/challenge` bez body zwraca 200:

   ```json
   {
     "clientId": "REPLACE_WITH_WEB_CLIENT_ID.apps.googleusercontent.com",
     "nonce": "SERVER_GENERATED_NONCE",
     "expiresAt": "2026-11-10T12:05:00Z"
   }
   ```

   Jednocześnie ustawia cookie `4m_google_challenge`: HttpOnly, SameSite=Strict,
   Path=/api/users/google i Secure przy HTTPS. Cookie nie trafia do JSON-a.
   Frontend przekazuje nonce do Google Identity Services i wysyła cookie
   automatycznie przy kolejnych żądaniach do API. Przy różnych originach fetch
   wymaga `credentials: 'include'` i jawnej konfiguracji CORS dopuszczającej
   właściwy origin oraz credentials. Obecny host nie konfiguruje CORS; integracja
   frontendu musi to zapewnić albo użyć proxy pod wspólnym originem.
   SameSite=Strict wymaga wspólnego site; mieszanie HTTP i HTTPS może go naruszyć.
   Za reverse proxy trzeba poprawnie obsłużyć zaufane forwarded headers, aby API
   rozpoznawało HTTPS. Nie przesyłaj browser tokenu jako pola JSON.

2. `POST /api/users/google/login` przyjmuje `{ "idToken": "GOOGLE_ID_TOKEN" }`.
   Token musi mieć podpis RS256 z klucza Google, właściwego wystawcę, audience
   równe ClientId, ważny termin, sub, poprawny e-mail i zgodne nonce. Metadane
   oraz klucze są pobierane przez HTTPS i buforowane przez IdentityModel;
   nieznany klucz zleca odświeżenie na kolejną próbę.
   Sukces 200 ma ten sam `AuthenticationResponse` co login hasłem.
   Brak konta dla sub daje 409 z `code=google_registration_required`; challenge
   pozostaje dostępny do rejestracji z tym samym ID tokenem przed wygaśnięciem.

3. `POST /api/users/google/register` przyjmuje:

   ```json
   {
     "idToken": "GOOGLE_ID_TOKEN",
     "login": "google.user",
     "name": "Anna",
     "surname": "Testowa",
     "birthDate": "2000-01-01"
   }
   ```

   E-mail i GoogleSubject pochodzą wyłącznie ze zweryfikowanego tokenu. Pozostałe
   dane mają walidację jak lokalna rejestracja; login jest wymagany, bez `@`.
   Odpowiedź 201 zawiera `user` (`RegisterUserResponse`), `authentication`
   (`AuthenticationResponse` albo null) i `requiresEmailConfirmation`.
   `email_verified=true` oraz adres @gmail.com albo niepuste hd Google Workspace
   pozwalają od razu potwierdzić adres i utworzyć sesję. Inne adresy wymagają
   naszego SMTP: authentication=null, requiresEmailConfirmation=true; użyj
   confirm-email/resend-confirmation, a następnie nowego logowania Google.
   Awaria SMTP daje 503 z `code=email_delivery_failed`, ale konto i token już
   istnieją. Konto bez hasła nie może się zalogować lokalnie przed ustawieniem
   pierwszego hasła przez forgot-password/reset-password.

Konto jest odnajdywane wyłącznie po GoogleSubject, również gdy adres w Google
się zmieni. Zapisany e-mail nie jest automatycznie nadpisywany. Nie ma łączenia
istniejącego konta lokalnego po zgodnym adresie ani odłączania Google.
Zajęty login/aktywny e-mail lub już zarejestrowany sub daje 409 z kodem
`login_or_email_in_use` lub `google_account_already_exists`. Nieaktywne konto
daje 403 (`account_inactive`); niepotwierdzony e-mail przy loginie daje 403
(`email_confirmation_required`). Błędny token, brak cookie, użyty lub wygasły
challenge daje 401 (`invalid_google_credentials`). Wyłączona integracja lub
niedostępne metadane Google zwracają 503. Walidacja DTO zwraca 400.

Wszystkie trzy endpointy mają Cache-Control: no-store. Challenge i login dzielą
limit user-login, rejestracja używa user-email; przekroczenie daje 429.
Challenge jest jednorazowo zużywany przed zapisem konta/sesji. Po błędzie zapisu
lub konflikcie wykrytym dopiero przez bazę trzeba pobrać nowy challenge i token.
Przy skutecznym logowaniu/rejestracji cookie jest usuwane. Domyślne opcje:
ChallengeLifetimeMinutes=5, MaxPendingChallenges=10000, HttpTimeoutSeconds=10.
Magazyn challenge jest ograniczony i lokalny dla procesu: restart unieważnia
próby, wiele instancji wymaga sticky sessions lub przyszłego wspólnego magazynu.

`IUserSessionService` tworzy wspólną sesję dla logowania Google i hasłem.
Wywołujący blokuje rekord konta i zatwierdza transakcję; nowe logowanie usuwa
poprzednią sesję. Rejestracja Google zapisuje konto, grupę startową i sesję
atomowo. Przy rejestracji Google wymagającej potwierdzenia adresu SMTP działa
po commicie: awaria pozostawia konto, grupę i token, a użytkownik może ponowić
wysyłkę przez resend-confirmation. To wyjątek od wysyłki przed commitem
w zwykłej rejestracji oraz przy wystawianiu tokenów resetu hasła, zmiany e-maila,
dezaktywacji i ponownego potwierdzenia adresu. Model i warunki cofania migracji opisuje
[domena](domain.md#konto-i-tokeny-użytkownika), a ręczną pracę z tokenem i cookie
[Postman](../postman/README.md#google).

## Odczyt i edycja własnego profilu

`UserProfileController` udostępnia `GET /api/users/profile` i `PUT /api/users/profile`.
Oba endpointy wymagają `[Authorize]` i nagłówka `Authorization: Bearer ACCESS_TOKEN`.
ID użytkownika pochodzi wyłącznie z claimu `sub`; klient nie wskazuje konta do edycji.
Brak ważnej sesji albo niedostępne konto zwracają 401.

GET zwraca 200 z `id`, `login`, `name`, `surname`, `email`, `phone`, `birthDate`,
`icon`, `isEmailConfirmed`, `createdAt`, `lastLoginTime`, `hasPassword`, `isGoogleLinked`
i `iconStorageKey`. Odpowiedź nie zawiera
hasha hasła, tokenów ani identyfikatora integracji bankowej. Odpowiedzi profilu
mają Cache-Control: no-store.

PUT przyjmuje kompletny zestaw edytowalnych danych profilu:

```json
{
  "login": "nowylogin",
  "name": "Jan",
  "surname": "Kowalski",
  "phone": "+48123456789",
  "birthDate": "2000-01-01",
  "icon": null
}
```

Login (3–100 znaków, bez `@`) jest przycinany i zamieniany na małe litery; imię i nazwisko
są wymagane, przycinane i mają maksymalnie 100 znaków, zgodnie z rejestracją.
Data urodzenia jest wymagana i nie może być domyślną datą 0001-01-01 ani datą
przyszłą (według daty UTC). Telefon jest opcjonalny, walidowany przez `[Phone]`,
do 30 znaków. Pominięcie, null lub pusty tekst telefonu czyści telefon.
`icon` to opcjonalny klucz presetu do 2048 znaków, zweryfikowany w storage.
Pominięcie, null lub pusty tekst ikony zachowuje obecne zdjęcie; do usunięcia
służy jawne `removeIcon=true`. Jest to zmiana wcześniejszego zachowania User,
zgodna z aktualizacją ikon grup. Upload opisano poniżej.
Niepoprawne dane zwracają 400 z ValidationProblemDetails. E-mail, hasło, flagi
konta i dane integracyjne nie należą do kontraktu edycji i nie są aktualizowane.

Sukces PUT zwraca 200 z aktualnym profilem; zajęty login zwraca 409.
Pozostawienie własnego loginu jest dozwolone. Sprawdzenie unikalności jest
uzupełnione obsługą naruszenia indeksu w bazie przy równoległych żądaniach.
Serwis blokuje rekord konta przez GetByIdForUpdateAsync, a repozytorium
UpdateProfileAsync wykonuje natychmiastowy UPDATE wyłącznie pól profilu,
w transakcji zarządzanej przez serwis. Zmiana loginu zachowuje aktualną sesję;
przy następnym logowaniu należy podać nowy login. Zdjęcia wymagają migracji
`20261109191600_UserImprovements`.

Ręczna weryfikacja w Postmanie: po loginie pobierz profil (200), wyślij PUT
i ponownie GET (200 z nowymi danymi). Sprawdź ten sam login (200), login innego
konta (409), brak imienia/datę przyszłą/błędny telefon (400) oraz brak tokenu
lub token po logout (401).

## Zdjęcia profilu i prefiksy storage

`GET /api/users/profile/icon-presets` wymaga Bearer i zwraca tablicę
`{ key, url, expiresAt }`. Lista pochodzi z S3, z rozszerzeniami PNG/JPG/JPEG/WebP,
sortowana po kluczu; pusta lista daje 200 z `[]`. Presety muszą być wcześniej
umieszczone w skonfigurowanym bucketcie; aplikacja i seed ich nie uploadują.

`GET /api/users/profile/icon` zwraca `{ url, expiresAt }` dla aktualnego zdjęcia.
Brak wyboru lub nieistniejący preset daje 404. URL własnego pliku jest podpisywany
bez zapytania HEAD; nie potwierdza to jego istnienia w S3. Brak sesji/dostępnego
konta daje 401, awaria storage 503. URL-e są czasowe (domyślnie 15 minut), a oba
endpointy i odpowiedzi profilu mają Cache-Control: no-store.

`PUT /api/users/profile` nadal przyjmuje cały zestaw danych profilu. Wybór zdjęcia:

| Instrukcja | JSON | multipart/form-data |
| --- | --- | --- |
| Zachowaj zdjęcie | pomiń icon i removeIcon | pomiń pola zdjęcia |
| Wybierz preset | icon = key z listy presetów | pole tekstowe icon = key |
| Wgraj własne | niedostępne | plik iconFile |
| Usuń wybór | removeIcon = true | pole tekstowe removeIcon = true |

Jednoczesny plik, preset lub usunięcie daje 400. Klient nie może ustawiać
iconStorageKey, IconContent ani IconContentType przez JSON. W multipart pola
login/name/surname/birthDate pozostają wymagane, phone jest opcjonalne.
Nie ustawiaj ręcznie Content-Type multipart bez boundary — klient HTTP generuje
ten nagłówek. Całe żądanie ma limit 6 MiB, sam plik 5 MiB. Pusty plik daje 400,
przekroczenie limitu 413, niezgodny MIME/sygnatura albo nieobsługiwany typ 415.
Dozwolone MIME: image/png, image/jpeg, image/webp; walidacja sprawdza początek
pliku jak w Grouping, nie dekoduje całego obrazu. Błędne dane/preset dają 400,
zajęty login 409, awaria storage 503. Obie reprezentacje PUT zwracają profil 200.

Własne pliki mają klucze `users/{userId}/icons/{guid}` generowane przez serwer.
User.Icon przechowuje klucz presetu, User.IconStorageKey klucz własnego pliku;
CHECK c_user_icon_source zabrania ustawienia obu naraz. ID bierze ICurrentUser.
Zapis blokuje użytkownika, więc podmiany tego samego profilu są szeregowane.
Plik z nieudanego uploadu jest sprzątany; przy błędzie zapisu bazy nowy plik
jest usuwany dopiero po udanym rollbacku. Stare własne zdjęcie jest usuwane
dopiero po potwierdzonym commit, wyłącznie z własnego prefiksu użytkownika.
Presetów nie usuwamy. Błąd usuwania jest logowany bez szczegółów poświadczeń
i nie cofa zapisanego profilu; może pozostawić osierocony plik. Niepewny wynik
commita zachowuje oba pliki, aby nie usunąć zdjęcia wskazywanego przez bazę.
Nie ma wspólnej transakcji S3/PostgreSQL ani automatycznego workera sprzątania.

W obu szablonach `.env` i mapowaniu Compose dostępne są:

```dotenv
USER_ICON_PRESET_PREFIX=icons/presets/users/
GROUP_ICON_PRESET_PREFIX=icons/presets/groups/
RECEIPT_STORAGE_PREFIX=receipts/
```

To prefiksy kluczy w jednym skonfigurowanym STORAGE_BUCKET, nie osobne buckety.
Brak ustawień zachowuje powyższe wartości domyślne. Prefiksy presetów kończ `/`;
Receipt sam normalizuje końcowy ukośnik. Zmiana RECEIPT_STORAGE_PREFIX dotyczy
nowych paragonów; odczyt/usuwanie korzystają z zapisanego StorageKey.
Zmiana prefiksu presetów zmienia katalog dostępnych wyborów. Po zmianie `.env`
uruchom ponownie API, a w Compose odtwórz kontener backendu.

Archiwizacja grupy pozostawia jej plik w S3. Podmiana/removeIcon grupy sprząta
stare własne zdjęcie po końcowym commit; presetów grup również nie usuwamy.

## Zmiana adresu e-mail

Konta z `GoogleSubject` nie mogą zmieniać e-maila, również po ustawieniu hasła.
Żądanie zwraca 403 z `code=google_account_email_change_forbidden`.
Potwierdzenie wcześniej wystawionego tokenu zmiany adresu jest odrzucane (400).

1. `POST /api/users/profile/change-email` wymaga aktywnej sesji
   (`Authorization: Bearer ACCESS_TOKEN`) i aktualnego hasła w JSON-ie:

   ```json
   {
     "newEmail": "nowy@example.com",
     "currentPassword": "AKTUALNE_HASLO"
   }
   ```

   ID użytkownika pochodzi z JWT. Nowy adres jest przycinany, zamieniany na
   małe litery i walidowany tak jak podczas rejestracji (maksymalnie 254 znaki).
   Żądanie jest dostępne dla aktywnego konta z potwierdzonym obecnym adresem.
   Sukces zwraca 202 bez body; wiadomość SMTP z tokenem trafia na nowy adres.
   Dotychczasowy e-mail i sesja pozostają aktywne do potwierdzenia.

2. `POST /api/users/confirm-email-change` jest anonimowy i przyjmuje JSON:

   ```json
   {
     "token": "TOKEN_Z_WIADOMOSCI"
   }
   ```

   Sukces zwraca 204, zmienia User.Email, zachowuje IsEmailConfirmed = true
   i usuwa wszystkie tokeny konta, w tym sesję. Użytkownik musi zalogować się
   ponownie dotychczasowym loginem i hasłem. Stare JWT i refresh token przestają
   działać. Potwierdzenie nie wymaga JWT, bo uwierzytelnia je token z wiadomości.

Żądanie zwraca 400 dla błędnego hasła, niepoprawnych danych lub adresu takiego
jak obecny; 401 dla niedostępnego konta/braku sesji; 409 dla zajętego adresu.
Potwierdzenie zwraca 400 dla tokenu niepoprawnego, wygasłego, zastąpionego,
zużytego lub innego przeznaczenia oraz 409, gdy adres został już zajęty.
Adres oczekujący nie jest rezerwowany, więc unikalność jest sprawdzana ponownie
przy potwierdzeniu, z obsługą konfliktu indeksu c_user_unique_email w bazie.
Zajętość adresu dotyczy tylko aktywnych kont; adres nieaktywnego konta jest dostępny.
Konflikt wycofuje transakcję, również zużycie tokenu; obecny adres się nie zmienia.

Token ma Purpose = EmailChange i nowy adres w TargetEmail. W bazie zapisujemy
tylko hash SHA-256, bez jawnego tokenu i bez PendingEmail na koncie. Domyślna
ważność to 24 h, odstęp między żądaniami na konto 60 s. Ponowienie żądania po
tym odstępie zastępuje token, także gdy wskazano inny nowy adres. Odstęp jest
wspólny dla wszystkich żądań zmiany e-maila danego konta; przekroczenie zwraca 429.
Oba endpointy korzystają również ze wspólnego limitu user-email
(5 żądań/minutę/IP, razem z rejestracją i resend-confirmation).
Opcje EmailChange są wiązane z konfiguracją i walidowane przy starcie:

```dotenv
EmailChange__TokenLifetimeHours=24
EmailChange__ResendCooldownSeconds=60
```

Wysyłka wykorzystuje istniejącą konfigurację Smtp i wspólny IEmailSender.
Temat i treść tworzy UserEmailMessages.EmailChange, a błąd wysyłki obsługuje EmailChangeService.
Awaria SMTP zwraca 503; poprzedni adres i poprzedni token pozostają bez zmian.
Nieudana wysyłka nie rozpoczyna nowego odstępu między żądaniami.
Token nie jest zwracany w API ani logowany. Nie ma automatycznych ponowień SMTP.

Request i confirm blokują najpierw użytkownika, następnie operują na tokenach.
TryConsumeAsync warunkowo usuwa aktualny niewygasły token. TryChangeEmailAsync
wykonuje warunkowy UPDATE konta; RemoveByUserAsync i SaveChangesAsync usuwają
pozostałe tokeny. Wszystkie operacje potwierdzenia są w jednej transakcji,
a SMTP przy żądaniu działa przed zatwierdzeniem zapisu. Migracja nie jest potrzebna.

Ręczna weryfikacja w Postmanie: login, żądanie zmiany (202), GET profilu ze starym
adresem (200), potwierdzenie tokenem z nowej skrzynki (204), GET starym JWT (401),
ponowny login i GET z nowym adresem (200), ponowne użycie tokenu (400).
Dodatkowo sprawdź błędne hasło/ten sam adres (400), zajęty adres (409), odstęp
między żądaniami (429), zastąpiony/wygasły token (400), awarię SMTP (503) oraz
konflikt zajęcia adresu pomiędzy żądaniem a potwierdzeniem (409 bez zmiany konta).

## Reset zapomnianego hasła

Oba endpointy PasswordResetController są anonimowe i mają Cache-Control: no-store.

1. `POST /api/users/forgot-password` przyjmuje:

   ```json
   {
     "email": "konto@example.com"
   }
   ```

   Adres jest przycinany i zamieniany na małe litery. Dla aktywnego konta
   z potwierdzonym e-mailem zapisujemy hash tokenu PasswordReset, TargetEmail
   równy aktualnemu adresowi, CreatedAt i ExpiresAt. SMTP działa przed zatwierdzeniem
   transakcji. Przycisk w wiadomości HTML otwiera formularz nowego hasła z tokenem.
   Błąd wysyłki wycofuje nowy token i zachowuje poprzedni, jeżeli istniał.

   Dla konta Google bez hasła ten sam przepływ ustawia pierwsze hasło lokalne.
   SMTP wysyła wtedy HTML `UserEmailMessages.PasswordSetup` z linkiem
   `?action=reset#token=...` do tego samego formularza; dalszy krok to zwykły
   `reset-password`. Hasło Google pozostaje niezależne. Po zatwierdzeniu wszystkie
   sesje są unieważniane; można zalogować się przez Google albo login/e-mail i hasło.

   Poprawne żądanie zwraca 202 bez body również dla nieznanego adresu,
   nieaktywnego lub niepotwierdzonego konta i w okresie między wysyłkami.
   Awaria SMTP także zachowuje 202; PasswordResetService zapisuje tylko
   ogólne ostrzeżenie, bez adresu, tokenu, hasła i szczegółów wyjątku SMTP.
   Status i treść odpowiedzi nie wskazują, czy istnieje konto; czasy odpowiedzi
   nie są wyrównywane, ponieważ wysyłka SMTP jest wykonywana w ramach żądania.
   202 nie gwarantuje dostarczenia wiadomości. Nieudana wysyłka nie rozpoczyna
   nowego odstępu; nie ma automatycznych ponowień wysyłki.

2. `POST /api/users/reset-password` przyjmuje:

   ```json
   {
     "token": "TOKEN_Z_WIADOMOSCI",
     "newPassword": "NoweHaslo1!"
   }
   ```

   Nowe hasło podlega takiej samej walidacji jak przy rejestracji: 8–128 znaków,
   co najmniej jedna wielka litera, cyfra i znak specjalny. Token musi mieć
   43 znaki Base64Url. Sukces zwraca 204 bez body. Błędne dane zwracają 400
   ValidationProblemDetails, a token niepoprawny, wygasły, zużyty lub innego
   przeznaczenia zwraca 400 ProblemDetails.

   Reset wymaga aktywnego konta z potwierdzonym adresem zgodnym z TargetEmail.
   W jednej transakcji blokujemy użytkownika, warunkowo zużywamy aktualny
   niewygasły token, zapisujemy hash nowego hasła przez TryResetPasswordAsync
   i usuwamy wszystkie pozostałe tokeny konta przez RemoveByUserAsync oraz
   SaveChangesAsync. Usuwana jest również sesja i oczekująca zmiana e-maila.
   Stare JWT i refresh token nie działają; wymagane jest ponowne logowanie
   dotychczasowym loginem i nowym hasłem. Błąd zapisu wycofuje także zużycie tokenu.

Domyślna ważność tokenu to 30 minut, odstęp między wysyłkami na konto 60 sekund.
Po upływie odstępu nowe żądanie zastępuje poprzedni token; do tego czasu nic
nie wysyła i nie unieważnia poprzedniego tokenu. Samo żądanie resetu nie zmienia
hasła ani nie wylogowuje użytkownika. Opcje są wiązane z konfiguracją i walidowane
przy starcie, bez potrzeby dodawania ustawień dla wartości domyślnych:

```dotenv
PasswordReset__TokenLifetimeMinutes=30
PasswordReset__ResendCooldownSeconds=60
```

forgot-password korzysta z limitu user-email (5 żądań/minutę/IP, wspólnego
z rejestracją, ponowną wysyłką potwierdzenia i zmianą e-maila). reset-password
korzysta z osobnej polityki user-password-reset (domyślnie 10 żądań/minutę/IP).
Przekroczenie limitu IP zwraca 429. Limity są lokalne dla instancji aplikacji.
Migracja nie jest potrzebna; używamy istniejącego UserTokenPurpose.PasswordReset.

Ręczna weryfikacja w Postmanie: zachowaj tokeny sesji, wyślij forgot-password
(202), odbierz token z maila i ustaw inne hasło przez reset-password (204).
Sprawdź odrzucenie starego JWT na GET /api/users/profile (401), starego refresh
tokenu (401), starego hasła (401), ponownego użycia tokenu resetu (400)
i zaloguj się nowym hasłem (200). Uwzględnij osobne limity login, refresh i reset-password.
Dodatkowo sprawdź adres nieznany/niepotwierdzony (202 bez wysyłki), ponowienie
przed upływem odstępu (202 bez nowej wysyłki), zastąpiony/wygasły token (400),
słabe hasło (400 bez zużycia tokenu), token innego przeznaczenia (400),
równoległe użycie tokenu (tylko jedno 204) i awarię SMTP (202 bez zmiany hasła).

## Dezaktywacja konta

Przed użyciem zastosuj migrację `20261023131244_AddAccountDeactivationTokens` przez
`AppDbContext`. Rozszerza CHECK rodzaju tokenu o AccountDeactivation = 5 oraz
wymaga TargetEmail także dla tego rodzaju. Migracja nie jest stosowana automatycznie.
Jej Down usuwa oczekujące tokeny dezaktywacji przed przywróceniem CHECK dla 1–4;
nie przywraca aktywności kont już dezaktywowanych.
Opis w `../doc/Baza danych.txt` uwzględnia już purpose 5 i wymagany TargetEmail.
Z katalogu głównego repozytorium, po przygotowaniu właściwej
lokalnej bazy i `.env` API, migracje stosuje się poleceniem:

```bash
dotnet run --file tools/ef.cs -- database update
```

1. `POST /api/users/profile/deactivate` z `Authorization: Bearer ACCESS_TOKEN`,
   bez body: 202 po wysyłce tokenu na aktualny adres. Konto pozostaje aktywne.
   Brak sesji/niedostępne konto daje 401, cooldown lub limit IP 429, awaria SMTP 503.
2. `POST /api/users/confirm-deactivation`, anonimowo, JSON `{"token":"TOKEN_Z_MAILA"}`:
   204 po dezaktywacji; token błędny, wygasły, zastąpiony lub użyty daje 400.
   Token innego przeznaczenia oraz niezgodny aktualny adres także dają 400.
3. Potwierdzenie atomowo ustawia `is_active=false` i usuwa wszystkie wpisy
   `user_token` tego konta. Zapis obejmuje blokadę użytkownika FOR UPDATE,
   warunkowe zużycie tokenu i zmianę IsActive w jednej transakcji.

Żądanie dezaktywacji nie wymaga hasła w body; wymaga aktualnej sesji,
a potwierdzenie posiadania adresu następuje przez token wysłany e-mailem.
Przycisk w wiadomości HTML otwiera frontend, który automatycznie wykonuje
POST confirm-deactivation i pokazuje wynik. Samo pobranie HTML przez GET nie
zmienia konta; potwierdzenie wykonuje JavaScript strony. Wiadomość uprzedza
o skutku kliknięcia. Awaria SMTP wycofuje nowy token, zachowując poprzedni.

Token jest generowany kryptograficznie (43 znaki Base64Url), w bazie zostaje
tylko SHA-256. Domyślnie ważność wynosi 30 minut, odstęp między wysyłkami 60 sekund.
Po odstępie nowe żądanie zastępuje poprzedni token. Opcje AccountDeactivation
są wiązane z konfiguracją i walidowane przy starcie; bez nadpisania obowiązują
wartości z klasy. Opcjonalne klucze: `AccountDeactivation__TokenLifetimeMinutes`
i `AccountDeactivation__ResendCooldownSeconds`. Oba endpointy mają no-store
i wspólny limit `user-email` (5/min/IP).

W Postmanie po 202 sprawdź, że profil nadal działa. Po 204 sprawdź stare JWT
na profilu (401), stary refresh token (401), ponowne potwierdzenie (400)
oraz logowanie loginem i poprawnym hasłem (403, `Account is deactivated.`).
Logowanie e-mailem wyszukuje tylko aktywne konta i daje 401, jeśli żadne go nie używa.
W bazie konto powinno mieć IsActive = false i zero tokenów niezależnie od rodzaju.
Profil, zmiana e-maila, potwierdzanie e-maila, reset hasła i refresh sprawdzają
aktywność konta; walidacja JWT sprawdza sesję oraz IsActive przy każdym
uwierzytelnieniu. Forgot-password dla nieaktywnego konta pozostawia ogólne 202
bez wysyłki. Nie ma reaktywacji ani fizycznego usuwania konta.

## Konfiguracja SMTP

Linki ustawia `UserEmailLinks:FrontendBaseUrl` w appsettings.json (domyślnie
`http://localhost:5173`), nadpisywane przez `UserEmailLinks__FrontendBaseUrl`.
W Compose odpowiada temu `USER_EMAIL_FRONTEND_URL` w containers/local/.env.
Podaj adres frontendu dostępny z przeglądarki odbiorcy, np. adres Tailscale,
bez query i fragmentu. Host waliduje HTTP/HTTPS przy starcie.
Link ma postać `/?action=confirm-email#token=...`; pozostałe akcje to
`confirm-email-change`, `confirm-deactivation` i `reset`. Frontend odczytuje
fragment, wywołuje API i usuwa token z adresu po sukcesie. Reset wymaga wpisania
nowego hasła; pozostałe potwierdzenia wykonują się po otwarciu strony.
Zmiana e-maila, reset hasła i dezaktywacja czyszczą lokalną sesję po sukcesie.
Po żądaniu wiadomości formularze przypominają o sprawdzeniu folderu Spam/Oferty.

Do ręcznych testów POST w Postmanie skopiuj i zdekoduj wartość `token` z fragmentu
linku, zamiast klikać przycisk: otwarcie strony może zużyć jednorazowy token.

`IEmailSender` z modułu User ma implementację `SmtpEmailSender` opartą na MailKit.
Interfejs, implementacja i `EmailDeliveryException` są razem w `Integrations/Smtp`.
Enum `SmtpSecurityMode` jest zdefiniowany w pliku `SmtpOptions.cs`.
Wysyła wiadomości HTML z tekstowym wariantem alternatywnym; każda wysyłka
tworzy osobne połączenie SMTP.
Wszystkie przepływy konta korzystają bezpośrednio z tego samego IEmailSender.
Tematy i treści są w Integrations/Smtp/UserEmailMessages.cs: EmailConfirmation,
EmailChange, PasswordReset, PasswordSetup i AccountDeactivation zwracają EmailMessage (Subject, Body), którego
rekord jest w tym samym pliku UserEmailMessages.cs. Nie ma osobnego EmailMessage.cs,
folderu Services/Email ani senderów i interfejsów dla rodzajów wiadomości.
Obsługa EmailDeliveryException należy do serwisu danego przepływu: rejestracja,
resend-confirmation, zmiana e-maila i dezaktywacja zachowują 503, a forgot-password zachowuje
202 i ogólne ostrzeżenie bez danych konta. Anulowanie żądania jest propagowane.
Projekt User przypina zależność `System.Security.Cryptography.Pkcs` do 10.0.13,
aby użyć poprawionej wersji zależności pobieranej przez MailKit/MimeKit
([advisory Microsoft](https://github.com/advisories/GHSA-846f-f764-76mx)).
`appsettings.json` zawiera niesekretne ustawienia SMTP: przykładowy host i adres
nadawcy oraz domyślne port, tryb TLS, kontrolę odwołania certyfikatu, nazwę
nadawcy i timeout. Host i adres nadawcy trzeba dopasować do wybranego serwera.
Sekcja EmailConfirmation pozostaje bez konfiguracji w JSON.
Konfiguracja jest walidowana przy pierwszej
wysyłce, więc samo uruchomienie API nie wymaga skonfigurowanej skrzynki.

W środowisku procesu lub lokalnym `ZPI4M.Api/.env` ustaw np.:

```dotenv
SMTP__HOST=ADRES_SERWERA_SMTP
SMTP__PORT=587
SMTP__SECURITYMODE=StartTls
SMTP__CHECKCERTIFICATEREVOCATION=true
SMTP__USERNAME=LOGIN_SMTP
SMTP__PASSWORD="HASLO_SMTP"
SMTP__SENDEREMAIL=NADAWCA@example.com
SMTP__SENDERNAME=4Money
SMTP__TIMEOUTSECONDS=30
```

To placeholdery; wartości muszą odpowiadać konfiguracji wybranego dostawcy.
Aktualny ZPI4M.Api/.env.example zawiera tylko poświadczenia, w tym login
i hasło SMTP. Powyższe zmienne mogą opcjonalnie nadpisywać wartości JSON.
Nie przenoś sekretów z lokalnego .env
do szablonu. Konfiguracja ASP.NET Core dopasowuje klucze bez rozróżniania
wielkości liter; podwójny podkreślnik oddziela sekcję od nazwy ustawienia.
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

## Postman: testy integracji Tink

W katalogu [postman/](../postman/) są kolekcje User, Banking, Notifications i diagnostyki
Tink Sandbox oraz szablony środowisk. Kolekcja Banking rozpoczyna folder 10
żądaniem logowania User; zapisuje wyłącznie `accessToken` do `fourMoneyAccessToken`,
a Runner kontynuuje do rozpoczęcia próby. Błędy logowania zatrzymują run i nie
pozostawiają poprzedniego tokenu. Można też używać osobnej kolekcji User. Adapter Banking odczytuje `sub`
ze zweryfikowanego JWT; wspiera również `ClaimTypes.NameIdentifier`.
Przepływ bankowy wymaga ręcznego przejścia przez Tink Link.
Instrukcja: [postman/README.md](../postman/README.md) i [notes/TINK_POSTMAN.md](../notes/TINK_POSTMAN.md).

## Syntetyczne transakcje Tink Connector

[CLI Python](../tools/scripts/tink_connector.py) działa poza backendem i używa
środowiska uv `tools`. Komendy `plan`, `seed`, `resume`, `verify` oraz format
własnego presetu opisuje [instrukcja](../tools/scripts/tink_connector/README.md).
Używaj testowego klienta Tink i zachowaj manifest runu w ignorowanym `.cache`.
Utworzenie danych Connector nie zastępuje testu zgody bankowej ani Tink Link.

```bash
uv sync --project tools --group pre-commit --group connector --locked
uv run --project tools --group pre-commit --group connector --locked python tools/scripts/tink_connector.py plan --preset basic-pln
uv run --project tools --group pre-commit --group connector --locked python -m unittest discover -s tools/scripts/tests -v
```

Grupy `pre-commit` i `connector` współdzielą `tools/.venv`; zachowaj obie podczas
uruchamiania CLI bezpośrednio przez uv. Opcjonalny `tools/.env` wczytaj przez
`--env-file tools/.env` przekazane do uv przed `python`; jego
[pusty szablon](../tools/.env.example) nie zawiera sekretów. Dostęp do rzeczywistego
Connector v2 wymaga uprawnień aplikacji i osobnego potwierdzenia w Tink.

Powiązanie CLI z lokalnym kontem jest opcjonalne: `seed --four-money-user-id GUID`
zapisuje je automatycznie po sukcesie, a `link-db <runId> --four-money-user-id GUID`
odtwarza z ukończonego manifestu. `tools/.env` wymaga wtedy dodatkowego
`FOURMONEY_DATABASE_DSN` w formacie PostgreSQL/libpq; sekret nie jest argumentem
CLI i nie trafia do manifestu. Starsze manifesty są obsługiwane, a tryb bez
powiązania nadal nie wymaga bazy. `plan` pozostaje offline; `verify` nie zmienia
mapowania lokalnego. Testy powiązań wymagają `FOURMONEY_TEST_POSTGRES_DSN`
i używają własnych tymczasowych baz. Szczegóły: [CLI Connector](../tools/scripts/tink_connector/README.md).
Od 2026-10-25 zapis po sukcesie zastępuje również poprzednie `tink_user_id`
wskazanego konta. Nie tworzy brakujących użytkowników i nadal odrzuca ID należące
do innego konta. Istniejące połączenia Banking zachowują swoją tożsamość Tink;
po zmianie mapowania nowa tożsamość wymaga własnego przepływu Link.

## Profile Tink z JSON bez resetu bazy — 2026-11-06

[`tink_profiles.py`](../tools/scripts/tink_profiles/README.md) obsługuje
`plan`, `apply` i `resume`. Domyślny `users.json` mapuje preset profilu na GUID
istniejącego konta; `apply` tworzy świeże tożsamości i dane Tink, po weryfikacji
zastępując wyłącznie wskazane `tink_user_id`. Baza musi działać z zastosowanym
schematem, a cele muszą być aktywne i potwierdzone. Nakładka nie uruchamia
migracji ani seedera SQL. Batch zatrzymuje się przy pierwszym błędzie; wznowienie
korzysta z zamrożonych manifestów i zachowuje zakończone wpisy. Konfiguracja
pozostaje w `tools/.env`, jak dla Connector. Blokada bazy jest wspólna z resetem.

## Łączny reset SQL i Connector

[`reset_state.py`](../tools/scripts/reset_state.py) uruchamia istniejący seeder
SQL, tworzy świeże dane Tink i zapisuje `tink_user_id` według mapowania JSON.
Domyślnie obejmuje tylko `postman.banking`. Komendy `plan`, `reset`, `resume`,
format mapowania i konfigurację testów opisuje
[instrukcja](../tools/scripts/reset_state/README.md). Wymaga już zastosowanych
migracji, konfiguracji Tink i `FOURMONEY_DATABASE_DSN` dla lokalnej bazy.
Reset usuwa lokalne dane i sesje; zdalne zasoby poprzednich runów pozostają.

`database/seed_local.sql` to lokalny reset danych przez TRUNCATE, nie migracja
ani aktualizacja przyrostowa. Zachowuje `expense.category`; kategorie są
dodawane przez migracje EF i nie są ponownie wstawiane przez seed.
Obecny seed tworzy pięć syntetycznych kont:
seed_anna, seed_bartek, seed_celina, seed_dawid i postman.banking. Wspólne hasło
testowe to `TestPassword1!`; seed_dawid jest celowo nieaktywny. Te dane są
wyłącznie lokalne. Nie zmieniaj hasła bazy PostgreSQL na hasło kont aplikacji.
Po migracji `20261109191600_UserImprovements` INSERT-y seeda tworzą
również pięć grup startowych i członkostw (także dla seed_dawid).
Seed zachowuje je obok dwóch współdzielonych przykładów „Dom” i „Wyjazd”:
łącznie siedem grup. ID grup startowych są generowane, a ID dotychczasowych
przykładów pozostają stałe. Ponowne wykonanie seeda nie kumuluje grup.
Zmiana pliku seed nie modyfikuje istniejących rekordów; aktualizację danych
wykonuje się osobno, a pełny seed tylko po zaakceptowaniu utraty lokalnych danych.

Aktualizacja seeda z 2026-10-28 wymaga także migracji
`20261028184348_RenameReceiptS3LinkToStorageKeyAndMakeUnique`.
Paragon ma unikalny klucz `receipts/{Guid:N}` i wynik w formacie odpowiedzi
workera (`receipt`, `warnings`, `needs_review`). Klucz jest syntetyczny:
seed nie wysyła obrazu do storage, więc podpisany URL nie potwierdza istnienia
zdjęcia. Obciążenia bankowe mają kwoty ujemne, a powiązane wydatki dodatnie.
Seed nie wywołuje OCR ani Tink; zachowuje stałe ID, konta i przykłady inboxa.

## Inbox powiadomień

Wewnętrzny NotifyAsync zwraca Task<Guid?>. Wywołujący przekazuje kandydatów
na odbiorców; UserLookupService jednym zapytaniem wybiera aktywne ID.
Nieaktywne i nieistniejące konta są pomijane. Pusta lista lub brak aktywnych
kont daje null bez zapisu, a nie błąd operacji biznesowej. Guid.Empty i pozostałe
niepoprawne dane żądania nadal powodują błąd walidacji.

Program.cs przekazuje kontekstom Expense, Grouping, Notifications, Receipt i Banking tę samą
scoped instancję NpgsqlConnection, zwalnianą przez DI. IGroupingTransactionCoordinator
rozpoczyna transakcję, dołącza pozostałe konteksty przez EnlistAsync/UseTransactionAsync
i wykonuje CommitAsync dopiero po wszystkich zapisach.
NotifyAsync nadal wykonuje SaveChangesAsync. Nie uruchamiaj operacji na tym
połączeniu równolegle ani nie otwieraj kolejnej transakcji w wywołanym serwisie.
Tworzenie Expense aktualizuje agregaty i zapisuje ExpenseAdded w tej samej
transakcji. GroupExpenseNotificationService wybiera pozostałych aktualnych
członków grupy; NotifyAsync pomija nieaktywne konta. Edycja i usuwanie nie powiadamiają.

`GET /api/notifications` wymaga Bearer JWT. ID użytkownika dostarcza ICurrentUser;
endpoint nie przyjmuje userId klienta. Parametry query: pageNumber (domyślnie 1,
minimum 1), pageSize (domyślnie 20, zakres 1–100), isRead (opcjonalny bool).
Brak isRead oznacza wszystkie wpisy, false nieodczytane, true odczytane.
Niepoprawne parametry i offset większy niż int.MaxValue zwracają 400
ValidationProblemDetails. Brak poprawnej sesji zwraca 401.

Odpowiedź to PagedResult<NotificationResponse> z metadanymi paginacji.
Wpis zawiera id, title, text, type (liczba 0–4), createdAt, groupId, expenseId,
readAt oraz isRead wyliczone z readAt. Wyniki są zawsze od najnowszych,
z ID jako drugim kluczem sortowania. Pusty inbox zwraca 200 i pustą listę.
Pobieranie nie oznacza powiadomień jako odczytane i nie zwraca danych innych
odbiorców ani encji EF. NotifyAsync pozostaje kontraktem wewnętrznym;
nie ma publicznego endpointu tworzenia powiadomień.

`PUT /api/notifications/{notificationId}/read` oznacza pojedyncze powiadomienie
zalogowanego użytkownika jako odczytane. Nie wymaga body. Zwraca 204 również
przy ponowieniu, 404 dla nieistniejącego powiadomienia lub braku przypisania
do odbiorcy oraz 401 bez poprawnej sesji. Serwis przekazuje czas UTC;
repozytorium wykonuje natychmiast warunkowy UPDATE po obu ID i ReadAt IS NULL,
bez SaveChangesAsync. Pierwsza zapisana data pozostaje bez zmian, także przy
równoległych żądaniach. Przy braku aktualizacji sprawdzane jest istnienie
wpisu odbiorcy, aby odróżnić ponowienie od 404.

`PUT /api/notifications/read-all` oznacza wszystkie aktualnie nieodczytane
powiadomienia zalogowanego użytkownika, również poza bieżącą stroną inboxa.
Nie wymaga body i zwraca 204 także dla pustego inboxa lub przy ponowieniu;
bez poprawnej sesji zwraca 401. Jeden warunkowy UPDATE po userId i ReadAt IS NULL
ustawia wspólny czas UTC bez pobierania listy i bez nadpisywania wcześniejszych dat.
Nowe wpisy dodane po wykonaniu UPDATE pozostają nieodczytane.
Testy Notifications są już dodane na życzenie użytkownika: UnitTests, Tests
(HTTP z atrapą serwisu) oraz IntegrationTests (PostgreSQL i prawdziwe JWT).
Do ręcznej weryfikacji służy kolekcja `postman/collections/4Money.Notifications.postman_collection.json`
ze wspólnym środowiskiem 4Money.Local i tokenem z loginu User. Zmienne
notificationPageNumber, notificationPageSize i notificationId opisuje postman/README.md.

## Modele i migracje

Zmiany wykonuj w modelach i konfiguracjach EF, uwzględniając kontekst modułu
oraz wspólny `AppDbContext`. Z katalogu głównego repozytorium:

```bash
dotnet tool restore
dotnet run --file tools/ef.cs -- migrations list
dotnet run --file tools/ef.cs -- migrations has-pending-model-changes
dotnet run --file tools/ef.cs -- migrations add NazwaZmiany
dotnet run --file tools/ef.cs -- database update
```

`list` pokazuje migracje i ich zastosowanie w skonfigurowanej bazie.
`has-pending-model-changes` sprawdza zmiany modelu względem snapshotu,
bez stosowania migracji. `add` tworzy migrację i aktualizuje snapshot;
przejrzyj wygenerowane operacje. `database update` zmienia wskazaną bazę.
Nowa baza wymaga SQL init → migracje → opcjonalny seed. Wrapper nie wykonuje
init ani seeda automatycznie.

Wrapper ustawia projekt `ZPI4M.Database`, startup `ZPI4M.Api` i `AppDbContext`.
Uruchamia EF z katalogu API, aby host odnalazł lokalny `.env`. Nadal wymagane są
poprawne opcje hosta (baza, JWT, storage); użyj lokalnej konfiguracji API lub
zmiennych środowiskowych. Obecne `Env.Load()` nadpisuje jednak zmienne
zdefiniowane również w `.env`. Aby jawnie wybrać inną bazę dla `migrations list`
lub `database update`, przekaż opcję EF `--connection`. Względne ścieżki
argumentów EF są liczone od API.
Jawne `--project/-p`, `--startup-project/-s` i `--context/-c` zastępują domyślne.
Wywołując z podkatalogu, dostosuj ścieżkę do pliku `tools/ef.cs`.

`dotnet tool restore` przywraca dokładnie przypiętą wersję, bez nadpisania wersji.
Jednorazowo można użyć innej wersji bez zmiany manifestu:

```bash
dotnet run --file tools/ef.cs -- --ef-version 10.0.5 migrations list
```

Opcja korzysta z `dotnet tool exec`, które może pobrać narzędzie z NuGet.
Wersja narzędzia nie zmienia wersji pakietów EF, SDK ani runtime aplikacji;
powinna pozostać zgodna z EF Core projektu. Numer SDK nie musi być identyczny
z wersją EF. Nie dodano `global.json`. Szczegóły i świadoma aktualizacja pinu:
[instrukcja narzędzi](../tools/README.md#ef-core-migrations).

Migracja `20261103203528_AddBankTransactionAssignments` tworzy rejestr przypisań
Banking bez backfillu. Zastosuj ją przed `database/seed_local.sql`; seed resetuje
i odtwarza przypisania syntetycznych wydatków i rozliczeń. Szczegóły zakresu są w
[kontrakcie Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#transaction-assignments--2026-11-03).

Pierwsza migracja jest pusta: projekt zakłada strukturę utworzoną przez SQL
inicjalizacyjny. Nie traktuj migracji jako samodzielnego sposobu zbudowania
całej bazy od zera. Następna migracja przenosi `expense_share` do `expense`.
Kolejne dodają `UserToken` i `IsEmailConfirmed`, usuwają stare pola refresh
tokenu z `User` i zmieniają mapowania dat na `timestamp with time zone`.
Historyczna migracja AddNotificationsConfigurationClass zakłada tabelę z SQL init i zmienia tylko
typ `sent_at`; jej snapshot zawiera już encję Notification.
Aktualny opis bazy (sprawdzony 2026-10-27) i kod Notifications rozszerzają ten
model o odbiorców, odczyt, tytuł, typy 0–4, created_at i fk_expense_id.
Migracja `20261027172612_AddNotificationsAfterRebase` jest już w historii
plików i zastępuje usuniętą `20261026233657_AddNotificationRecipient`.
Zachowuje daty przez RenameColumn, wypełnia title pustym tekstem i type wartością
0 oraz nie przypisuje automatycznie odbiorców do istniejących wpisów.
Snapshot uwzględnia Banking i nowy inbox. Obecność plików nie potwierdza
zastosowania migracji do aktualnej bazy użytkownika.
`UserEmailNotNull` ustawia wymagany e-mail. Migracja
`20261021162958_AddUniqueExpenseSourceReferences` dodaje osobne unikalne indeksy
na `expense.expense.bank_transaction_id` i `receipt_id`. Kolumny nadal
dopuszczają NULL. Istniejące powtórzenia niepustych FK uniemożliwią utworzenie
indeksów; przed zastosowaniem tej migracji sprawdź dane, jeśli baza nie jest pusta.
`20261023131244_AddAccountDeactivationTokens` rozszerza CHECK purpose i TargetEmail
w `user.user_token`; nie dodaje kolumny ani nowej tabeli.

Zmiany Banking z tej gałęzi obejmuje jedna migracja
`20261026215244_AddBanking`, po `AddAccountDeactivationTokens` z `master`.
Zastępuje wcześniejsze `AddBankConnections` i `AddBankRefreshes`; zachowuje
ręczne SQL usuwające historyczną unikalność rachunku i CHECK dodatniej kwoty.
Migracje z `master` pozostają bez zmian. Bazy z zastosowanymi starymi migracjami
tej gałęzi wymagają odtworzenia albo osobno uzgodnionego przejścia; nowa migracja
nie jest dla nich migracją przyrostową. Nie usuwaj samych wpisów historii EF,
pozostawiając istniejące tabele. `database/seed_local.sql` usuwa dane, ale nie
odtwarza schematu ani historii migracji, więc nie zastępuje odtworzenia bazy.
Pierwsza migracja nadal zakłada SQL init; `AddBanking` nie jest nowym baseline.

Nie edytuj SQL init ani zastosowanych migracji w ramach kolejnej zmiany modelu.
Migracja `20261109191600_UserImprovements` zastępuje globalną unikalność
e-maila indeksem częściowym `WHERE is_active = true`. Obsługuje ograniczenie
UNIQUE z SQL init i sam indeks. Cofnięcie migracji wymaga globalnie unikalnych
adresów; przy ponownie użytym adresie Down kończy się błędem bez usuwania danych.
Przy cofaniu wielu migracji EF może wcześniej zatwierdzić udane kroki Down;
błąd jednej migracji nie oznacza przywrócenia całego łańcucha do punktu startu.
Snapshot i plik Designer są generowane wraz z nową migracją i trafiają do Git.
Przy zmianie typu timestamp sprawdź strefę dotychczasowych danych. Sam typ
`DateTime` w C# nie określa typu SQL — decyduje konfiguracja EF.

Aktualny squashed łańcuch Expense opisuje [sekcja po rebase](#migracje-po-rebase-expensegrouping--2026-11-03).

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

Użyj świeżego katalogu wyników przy kolejnych uruchomieniach. Pomocnik wybiera
śledzone projekty `.UnitTests.csproj` i `.Tests.csproj` dla zestawu unit oraz
`.IntegrationTests.csproj` dla integration. Solucja zawiera po trzy projekty
User, Banking i Notifications, dwa Receipt, dwa Expense, Grouping i Shared (łącznie piętnaście projektów). Komunikat
„not configured” nie oznacza wykonania testów.

Workflow backendu uruchamia PostgreSQL i ustawia `TEST_POSTGRES_CONNECTION_STRING`
dla integracyjnych testów User, Banking, Notifications i Expense. Wszystkie zestawy tworzą
własne bazy o losowych nazwach na serwerze testowym.
Host nadal ładuje `.env` przez `Env.Load()`; testy HTTP używają własnych hostów.

Receipt.IntegrationTests zawiera ręczny test rzeczywistego S3, niezależny od
PostgreSQL. Wymaga działającego magazynu i `STORAGE_INTEGRATION_TESTS=1`;
odczytuje ustawienia `STORAGE_*` z lokalnego `.env` API oraz zmiennych środowiskowych.
Tworzy i usuwa syntetyczny obiekt. Nie włączaj go przy samej kontroli dokumentacji.
Obecny workflow backendu nie uruchamia SeaweedFS, workera ani testów pytest.

## Worker OCR i magazyn obrazów

Do uruchomienia workera poza Dockerem potrzebne są Python 3.12, uv co najmniej
0.12.23 (wymaganie `workers/extraction_worker/pyproject.toml`) oraz lokalny
`OPENROUTER_API_KEY`. Instrukcja i kontrakt znajdują się w
[README workera](../workers/extraction_worker/README.md); eksperymenty i ewaluacja
w [osobnej instrukcji](../workers/extraction_worker/experiments/README.md).
Z katalogu `workers/extraction_worker`:

```bash
uv sync --locked
uv run --env-file .env uvicorn api:app --host 127.0.0.1 --port 8000 --workers 1
```

Start rozgrzewa OCR i może pobierać brakujące modele. Wywołania ekstrakcji korzystają
z dostawcy zewnętrznego; `/health` nie potwierdza jakości OCR ani dostępu do modelu.
Testy korzystają z atrap OCR i dostawców; z tego samego katalogu:

```bash
uv run --extra test pytest -q --basetemp .cache/test-temp/pytest
```

Host .NET waliduje opcje OCR i S3 przez `ValidateOnStart`. Oprócz JWT wymaga
poprawnych `STORAGE_ACCESS_KEY` i `STORAGE_SECRET_KEY` oraz pozostałych opcji S3.
Domyślne opcje są w `appsettings.json`; API `.env` zawiera poświadczenia.
W Compose używane jest wyłącznie `containers/local/.env` i adresy usług wewnętrznych.
`STORAGE_PUBLIC_ENDPOINT` musi być dostępny dla odbiorcy podpisanego URL;
domyślne `http://localhost:8333` odpowiada lokalnemu mapowaniu portu S3.
Po zmianie portu lub przy dostępie z innego urządzenia dopasuj ten adres.

Kolekcja Storage w Postmanie sprawdza bezpośrednio S3; nie wymaga backendu ani JWT
i nie potwierdza integracji Receipt. Analiza i odczyt paragonu są dostępne przez
endpointy Expense, a jego zapis przez POST/PUT formularza wydatku.
Weryfikację z rzeczywistym OCR/storage opisuje [status](implementation-status.md).

## Integracyjne testy procesu Banking

Projekt `ZPI4M.Banking.IntegrationTests` pasuje do filtra CI. Wymaga jawnego
`TEST_POSTGRES_CONNECTION_STRING` wskazującego odizolowany serwer PostgreSQL. Testy tworzą
własną bazę o losowej nazwie, uruchamiają baseline SQL i migracje, a po zakończeniu
usuwają wyłącznie swoją bazę. Nie korzystają z `DB_LOCAL_CONNECTION_STRING`.
Bez konfiguracji testy są pomijane; nie raportuj ich wtedy jako wykonanych.
Workflow przekazuje PostgreSQL do wszystkich modułów przez wspólną
zmienną `TEST_POSTGRES_CONNECTION_STRING`.

```bash
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Banking.IntegrationTests/ZPI4M.Banking.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

Dalsze ustawienia i warunki testu z rzeczywistym Tink opisuje
[instrukcja Tink](../notes/TINK.md). Kod nie potwierdza aktywacji Continuous Access
na koncie. Nigdy nie uruchamiaj testów persystencji na serwerze produkcyjnym.
Testy Notifications używają tej samej zmiennej TEST_POSTGRES_CONNECTION_STRING.
Fixture stosuje SQL init, migracje i seed; sprawdza zachowanie starych dat oraz
brak automatycznie przypisanych odbiorców. Nie używa EnsureCreated ani EF InMemory.
Brak konfiguracji lokalnie pomija testy PostgreSQL, a w CI jest błędem.
Hosty testowe używają TestServer i jawnej konfiguracji DI, nie produkcyjnego .env.
Szczegóły opisuje [tests/README](../backend/ZPI4M.Backend/tests/README.md).
Nie dodawaj informacyjnych plików w notes.

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

- [Compose](../containers/local/docker-compose.yml), [host API](../backend/ZPI4M.Backend/src/ZPI4M.Api/Program.cs) i [profile uruchomienia](../backend/ZPI4M.Backend/src/ZPI4M.Api/Properties/launchSettings.json)
- [Migracje](../notes/MIGRATIONS.md)
- [Narzędzia, hooki i CI](../tools/README.md), [polska instrukcja hooków](../notes/PRECOMMIT.md)
- [Workflow backendu](../.github/workflows/backend.yml) i [pomocnik CI](../tools/ci/backend_checks.sh)

## Uruchomienie refresh on demand

Wymagana jest migracja `AddBanking`, istniejący `User.TinkUserId`
oraz dostęp aplikacji do scope’ów `accounts:read,transactions:read`.
Worker startuje z hostem API i czyta dane już dostępne w Tink.
Konfiguracja `Banking:Refresh` i procedura testów ręcznych są opisane
w [kontrakcie Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand)
i [instrukcji Postmana](../notes/TINK_POSTMAN.md#refresh-danych-przez-backend-4money).
Testy PostgreSQL muszą wskazywać izolowany serwer przez `TEST_POSTGRES_CONNECTION_STRING`;
nie stosuj migracji na bazie deweloperskiej w ramach weryfikacji.

## Migracja zatwierdzonego modelu grup (2026-10-28)

Jedna nowa migracja `ApprovedGroupingModel`, designer i snapshot rozwijają
historyczny baseline bez edycji SQL init i starszych migracji. Up przed DDL
blokuje cztery zmieniane tabele i przerywa, jeżeli istnieją stare grupy,
członkostwa, wydatki lub udziały. Nie ma konwersji procentów, wyboru administratora,
waluty ani backfillu agregatów. Down również wymaga pustych danych, odtwarza
historyczne CHECK limitów i procentów; nie odtwarza usuniętych danych.
Nie czyść bazy deweloperskiej automatycznie po błędzie tej osłony.

Kolejność nowej bazy pozostaje: SQL init → wszystkie migracje → seed_local.sql.
Seed PLN obejmuje nowe rozliczenia, zaproszenie i limity oraz oblicza agregaty
ze źródeł. Reset wymaga aktualnego schematu i usuwa dane jak dotychczas.
Testy `GroupingModelTests` w Banking.IntegrationTests korzystają z losowych baz
przez TEST_POSTGRES_CONNECTION_STRING; obejmują Up/Down/Up, osłonę, seed,
repozytoria i ograniczenia. `GroupingModelContractTests` nie wymagają połączenia
z bazą i kontrolują model/snapshot, granice modułów oraz operacje migracji.

### Uzupełnienie modelu członkostwa — 2026-10-30

Na wyraźne życzenie użytkownika istniejąca migracja ApprovedGroupingModel została
uzupełniona o `grouping.group_user.is_member boolean NOT NULL DEFAULT true`.
Baza z wcześniejszą wersją tej samej migracji nie dostanie kolumny przez zwykłe
`database update`: lokalne ponowne zastosowanie wymaga najpierw cofnięcia do
AddNotificationsAfterRebase z użyciem poprzedniej wersji migracji, następnie
builda i zastosowania wersji poprawionej. Osłona Down wymaga pustych tabel;
nie usuwaj danych ani historii EF w celu jej obejścia.

Storage jest współdzielony w ZPI4M.Shared.Storage: IObjectStorage, S3ObjectStorage,
FileUrlResult i konfiguracja STORAGE_*. Klucze generuje moduł wywołujący;
Receipt zachowuje prefiks receipts/. Testy adaptera: ZPI4M.Shared.UnitTests.

## Grupy: opis, obrazy i archiwizacja — 2026-10-31
Nowa migracja AddGroupDescriptionIconsAndArchive dodaje description varchar(255)
nullable, icon_storage_key nullable oraz is_archived NOT NULL DEFAULT false.
Icon i IconStorageKey są rozłączne. Presety pochodzą z listowania storage pod
GROUP_ICON_PRESET_PREFIX (domyślnie icons/presets/groups/), bez listy ID w kodzie.
GroupIconService korzysta z Shared.Storage; moduł generuje klucze własnych plików.
POST/PUT grup obsługują JSON i multipart. Brak instrukcji ikony na PUT ją zachowuje;
removeIcon usuwa wybór. Opis jest przycinany, pusty staje się null, limit wynosi
255 znaków Unicode. DELETE archiwizuje, zachowując dane i pliki; odczyty ukrywają
archiwum. Szczegóły i ograniczenia: README modułu Grouping.
Migracja nie została zastosowana na bazie deweloperskiej.

## Koordynacja zapisów Grouping — 2026-11-01
Wszystkie operacje zapisu Grouping korzystają ze scoped
IGroupingTransactionCoordinator. Zewnętrzny przepływ otacza operacje przez
ExecuteAsync(action, succeeded, cancellationToken); wywołania zagnieżdżone
współdzielą transakcję i nie zatwierdzają jej samodzielnie. succeeded dla
wyników serwisów oznacza status 2xx. Odmowa lub wyjątek zagnieżdżony blokuje
końcowy commit nawet po przechwyceniu błędu. Walidacyjne odmowy przed zapisem
są zgłaszane do koordynatora przez Reject.

Nie rozpoczynaj bezpośrednio transakcji EF przed wejściem do koordynatora:
taka transakcja jest odrzucana przed mutacją. SaveChangesAsync nadal zapisuje
wszystkie oczekujące zmiany kontekstu jako jedną jednostkę pracy. Operacje
na wspólnym połączeniu wykonuj kolejno, bez równoległego używania zakresu DI.

Po rollbacku zakończ użycie zakresu DI i utwórz nowe konteksty. Koordynator
odrzuca kolejne zapisy w zakończonej niepowodzeniem jednostce pracy.
Nie czyści globalnie ChangeTracker ani nie naprawia stanu encji w pamięci.
Notifications jest dołączany jawnie do tej samej transakcji i połączenia;
właściciel transakcji odpowiada za odłączenie.

AfterCommit służy usunięciu zastąpionych własnych ikon dopiero po końcowym
commicie. AfterRollback sprząta nowe uploady tylko po potwierdzonym rollbacku.
Przy niepewnym wyniku commitu zachowujemy pliki. Cleanup jest best effort,
nie zmienia potwierdzonego sukcesu. Brak trwałej kolejki/outboxa.

## Migracje po rebase Expense/Grouping — 2026-11-03

Po `20261102215301_AddGroupingModel` występuje
`20261103165622_CompleteExpenseModel`, wygenerowana z końcowego modelu Expense.
Usunięto tymczasową `AddTemporaryExpenseAggregationSupport` oraz zastąpiono
siedem migracji gałęzi Expense jedną. Grouping/Limits i migracja AddGroupingModel
z master pozostają bez zmian. Po Expense następuje `20261103203528_AddBankTransactionAssignments`. Designer Banking i snapshot uwzględniają oba modele.

Expense zachowuje `_id`, `name`, `transaction_time`, `fk_*`, wymaganego
`FkPayerId`, `Version`, metadane pozycji, kategorie bez Color i udziały procentowe
`Percentage` numeric(5,2). Kwoty udziałów są wyliczane przez Expense.
Migracja uwzględnia ograniczenia historycznego SQL init, których nie śledził
snapshot: zachowuje istniejące FK i CHECK ilości, usuwa CHECK dodatniej ceny,
a Down przywraca FK przypisania pozycji do użytkownika i CHECK ceny.
Up/Down wymagają pustych wydatków, pozycji i udziałów; nie konwertują danych ani
nie zgadują płatnika. Konflikty kategorii powodują rollback migracji.
Kolejność pustej bazy: SQL init → wszystkie migracje → seed. Seed przelicza
syntetyczne agregaty Grouping z procentów; nie utrzymuje ich podczas działania API.
Baza, która wykonała usuniętą tymczasową migrację, wymaga odtworzenia albo osobnej migracji przejściowej.

Od 2026-11-03 tworzenie Expense jest połączone z koordynatorem i aktualizacją
agregatów oraz rzeczywistymi metadanymi. Odpowiedzialności i kolejność zapisów
opisuje [architektura](architecture.md#expense--grouping--integracja-agregatów-2026-11-03).
Nie ma migracji dla tej integracji. Nie uruchamiaj seeda ani odbudowy bazy aplikacji
w ramach sprawdzenia kodu; testy używają izolowanych baz na osobnym serwerze.

## Użytkownik Tink przy łączeniu banku — 2026-11-07

Po `register` → `confirm-email` → `login` klient może rozpocząć połączenie przez
`POST /api/banking/connection-attempts` z JWT User, bez body. Backend tworzy lub
odzyskuje brakujące mapowanie Tink; sukces zwraca HTTP 201 z linkiem autoryzacji.
Po 503 ponów rozpoczęcie połączenia w tej samej sesji: powstanie nowa próba,
a zapisane mapowanie będzie użyte ponownie. Nie wykonuj ponownej rejestracji.
Osobny endpoint `/api/banking/tink-user` usunięto. W kolekcji Banking po loginie
10 następuje bezpośrednio rozpoczęcie połączenia 11.
[Pełny kontrakt](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#tink-user-initialization-on-connection-start--2026-11-07).
