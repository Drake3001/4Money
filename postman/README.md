# Postman — 4Money i diagnostyka Tink

Kolekcje User i Notifications odpowiadają obecnej implementacji tych modułów.
Kolekcje Banking i Tink obejmują połączenia bankowe oraz diagnostykę dostawcy;
ich obecność nie potwierdza wykonania rzeczywistego przepływu Sandbox.

| Kolekcja | Środowisko | Przeznaczenie |
| --- | --- | --- |
| `collections/4Money.User.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Rejestracja lokalna i Google, potwierdzenia e-maila, login/refresh/logout, profil i zdjęcie, zmiana e-maila, reset hasła i dezaktywacja |
| `collections/4Money.Notifications.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Inbox z paginacją, wszystkie/nieodczytane/odczytane, oznaczanie jednego lub wszystkich jako odczytane |
| `collections/4Money.Reports.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Cztery raporty GET: kategorie, okresy/kategorie, średnie dni tygodnia i kalendarz dat; przykłady personal/group, pustego wyboru oraz progów agregacji |
| `collections/4Money.Banking.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Logowanie 4Money, rozpoczęcie próby, odczyt rezultatu, połączeń, zgód i błędy callbacku |
| `collections/Tink.Diagnostics.postman_collection.json` | `environments/Tink.Sandbox.example.postman_environment.json` | Token aplikacji, token użytkownika, zgody oraz osobne delegowanie Tink Link |
| `collections/4Money.Storage.postman_collection.json` | `environments/4Money.Storage.Local.example.postman_environment.json` | Lokalny SeaweedFS S3 na http://localhost:8333: listowanie, upload, pobranie i usunięcie obiektu testowego |

Dla Storage ustaw lokalnie `storageAccessKey` i `storageSecretKey` zgodnie
z `STORAGE_ACCESS_KEY` i `STORAGE_SECRET_KEY` w `containers/local/.env`.
W już zaimportowanym środowisku ustaw `storageEndpoint=http://localhost:8333`
albo zaimportuj nowy szablon Storage.Local. Kolekcja nie wymaga backendu API.

Folder **80 — Odczyt rachunków i transakcji** w kolekcji Banking obejmuje nowe
lokalne endpointy GET list i szczegółów. Wymaga JWT User, bez Tink i synchronizacji.
Listy zapisują `bankReadAccountId` / `bankReadTransactionId` do żądań szczegółów;
puste listy czyszczą poprzednie ID. Uruchamiaj przez Send: 81 → 82 i 83 → 84.
Opcjonalne filtry w Params są domyślnie wyłączone; kwoty odpowiedzi są tekstem.
Szczegóły: [instrukcja odczytu](../notes/TINK_POSTMAN.md#odczyt-lokalnych-rachunków-i-transakcji).

Importuj kolekcję i właściwy szablon środowiska. Wartości poufne uzupełniaj
wyłącznie lokalnie. Kolekcje używają formatu v2.1 oraz aktualnego Postman Desktop
z obsługą `pm.execution.skipRequest`, `pm.execution.setNextRequest` i `URL`.

## Moduł User

Do lokalnych testów bez rejestracji i SMTP zastosuj po migracjach
[database/seed_local.sql](../database/seed_local.sql). To pełny reset danych
aplikacji, w tym sesji, połączeń Banking i mapowań Tink. Odtwarza pięć kont:
`seed_anna`, `seed_bartek`, `seed_celina`, `seed_dawid` i `postman.banking`.
Wspólne hasło testowe to `TestPassword1!`. Konto `seed_dawid` pozostaje nieaktywne;
pozostałe są aktywne i mają potwierdzony e-mail.

Dla Banking ustaw `userLogin=postman.banking` i `userPassword=TestPassword1!`,
a następnie wyślij **21 — Zaloguj**. Login zapisze tokeny używane przez Banking.
Rejestracja i potwierdzanie e-maila nie są potrzebne dla kont z seedera.
Jeśli wcześniej powiązano konto z Tink, po resecie odtwórz mapowanie przez
`link-db` z zachowanego manifestu; opisuje to instrukcja CLI Connector.

Wspólny szablon `environments/4Money.Local.example.postman_environment.json`
zawiera zmienne User, Banking i Notifications. Aktualizacja pliku w repozytorium nie aktualizuje
wcześniej zaimportowanego środowiska w aplikacji Postman; zaimportuj aktualny
plik lub dodaj brakujące zmienne w wybranym środowisku.

1. Zaimportuj kolekcję User i szablon środowiska `4Money.Local`, a następnie
   wybierz to środowisko. Domyślne `baseUrl=http://localhost:5283` odpowiada
   backendowi w Compose i zwykłemu `dotnet run` (profil `http`), bez `/api`.
   Jeśli masz już zaimportowane środowisko, zmień w nim `baseUrl` na ten adres;
   zmiana szablonu w repo nie aktualizuje wartości w Postmanie.
   Backend wymaga lokalnej konfiguracji SMTP,
   JWT, storage i bazy ze wszystkimi aktualnymi migracjami, łącznie
   z `20261109191600_UserImprovements`.
2. Uzupełnij lokalnie `userLogin`, `userEmail` (dostępna skrzynka), `userPassword`
   i dane profilu. Hasło ma mieć 8–128 znaków, wielką literę, cyfrę i znak specjalny.
   `userBirthDate` ma format `YYYY-MM-DD`. Nie eksportuj prawdziwych danych do Git.
3. Uruchom rejestrację przez **Send**. Token z fragmentu `#token=...` linku w mailu wklej do
   `userEmailConfirmationToken`, wyślij potwierdzenie, potem login.
   Nie otwieraj wcześniej linku w przeglądarce: frontend automatycznie zużywa
   token potwierdzenia. Ta sama zasada dotyczy zmiany e-maila i dezaktywacji.
   Przy błędzie SMTP rejestracja wycofuje konto, a żądania nowych tokenów
   zachowują poprzednie tokeny. Można ponowić nieudaną rejestrację.
   Login zapisuje `fourMoneyAccessToken` i `fourMoneyRefreshToken` w środowisku.
   Żądania chronione pobierają Bearer automatycznie; refresh token trafia do body.
4. Pobierz lub edytuj profil. PUT wysyła komplet pól. Puste `userPhone`
   czyści telefon, a puste `userIcon` zachowuje aktualne zdjęcie. Do usunięcia
   zdjęcia służy removeIcon (żądanie 36). Logout wykonaj po operacjach chronionych
   albo zaloguj się ponownie przed kolejnym chronionym żądaniem.
5. Dla zmiany e-maila ustaw `userNewEmail`, a token z nowej skrzynki wklej do
   `userEmailChangeToken`. Potwierdzenie usuwa sesję i aktualizuje lokalne `userEmail`.
   Dla resetu hasła ustaw `userNewPassword`, poproś o reset i wklej token do
   `userPasswordResetToken`. Sukces aktualizuje lokalne `userPassword` i usuwa sesję.
   Po tych potwierdzeniach zaloguj się ponownie.
6. Dezaktywację wykonuj na końcu na koncie testowym. Żądanie wymaga Bearer,
   bez body; konto nadal działa. Token z maila wklej do `userDeactivationToken`
   i ręcznie wyślij potwierdzenie. Sukces ustawia `is_active=false` i usuwa wszystkie
   tokeny. Kolejny login po loginie i poprawnym haśle zwraca 403; próba przez
   e-mail bez aktywnego konta zwraca 401. Nie ma endpointu reaktywacji.

Każde żądanie User zatrzymuje Runner przez `setNextRequest(null)`; pełny run
nie wykonuje całego flow. Wybieraj żądania i używaj Send. Brak wymaganych zmiennych
pomija żądanie. Skrypty wstępne kodują JSON ze zmiennych przez `JSON.stringify`,
także dla haseł zawierających cudzysłowy lub ukośniki. Zmienne aktualizują się
po oczekiwanej odpowiedzi, a login przed wysłaniem czyści poprzednie tokeny sesji.
Skrypty sprawdzają status sukcesu; oczekiwane błędy podczas prób negatywnych
spowodują czerwony wynik testu sukcesu i należy wtedy sprawdzić ProblemDetails.

Limity na IP są osobne: `user-login` 30/min, `user-refresh` 120/min,
`user-password-reset` 10/min. `user-email` pozostaje wspólny: 5/min/IP.
Wyczerpanie jednej polityki nie blokuje pozostałych. Liczniki działają lokalnie
dla instancji w stałych oknach 60 s, bez kolejki; przekroczenie daje 429.
Błędne żądania też zużywają limit, a osoby za wspólnym IP dzielą licznik operacji.
Progi uwierzytelniania można nadpisać dodatnimi liczbami całkowitymi przez
`UserRateLimiting__LoginPermitLimit`, `UserRateLimiting__RefreshPermitLimit`
i `UserRateLimiting__PasswordResetPermitLimit`; błędne wartości blokują start API.
Cooldown wysyłek
na konto to domyślnie 60 s. Tokeny potwierdzenia i zmiany e-maila są ważne 24 h,
resetu i dezaktywacji 30 min. `forgot-password` zwraca 202 również bez wysyłki
(np. konto nieaktywne, cooldown lub awaria SMTP).

## Zdjęcie profilu

Folder 30 kolekcji User obejmuje teraz:

- **33 — Lista presetów zdjęcia**: GET icon-presets; skopiuj wybrany `key`
  do userIcon, następnie wyślij edycję profilu **32**. Pusty katalog daje `[]`.
- **34 — Aktualne zdjęcie — URL**: GET icon; zapisuje userIconUrl (tajne)
  i userIconUrlExpiresAt. Brak wyboru daje 404; URL wygasa domyślnie po 15 minutach.
- **35 — Wgraj zdjęcie profilu**: PUT multipart; wybierz plik ręcznie w
  Body → form-data → iconFile. Nie ustawiaj Content-Type ręcznie. Uzupełnij
  zwykłe pola profilu. PNG/JPEG/WebP, maks. 5 MiB; błędny MIME/sygnatura 415,
  zbyt duży plik 413. Nie łącz z presetem ani removeIcon.
- **36 — Usuń zdjęcie profilu**: PUT JSON z removeIcon=true i pełnymi danymi
  profilu. Czyści wybór; wspólny preset pozostaje w S3.

Odczyt profilu zwraca icon (preset) lub iconStorageKey (własny upload).
Puste/null/pominięte icon w edycji zachowuje zdjęcie. Zmiana wymaga jawnego
wyboru presetu, pliku lub usunięcia. Własne pliki są sprzątane po commit;
błąd sprzątania nie cofa aktualizacji. URL-e i tokeny trzymaj wyłącznie lokalnie.
Zaktualizowany szablon środowiska dodaje puste userIconStorageKey, userIconUrl
i userIconUrlExpiresAt. Import pliku nie aktualizuje automatycznie istniejącego
środowiska w Postmanie. Presety trzeba wgrać wcześniej pod skonfigurowany prefiks.

Prefiksy User, Grouping i Receipt oraz ograniczenia transakcji opisuje
[development](../agent-context/development.md#zdjęcia-profilu-i-prefiksy-storage).

## Google

Folder **70 — Rejestracja i logowanie Google** w kolekcji User zawiera challenge,
login i register. Wymaga włączonej integracji oraz migracji UserImprovements.
Szablon środowiska dodaje puste googleClientId, googleNonce,
googleChallengeExpiresAt i tajne googleIdToken. Aktualizacja pliku nie zmienia
wcześniej zaimportowanego środowiska — dodaj zmienne lub zaimportuj je ponownie.

1. Wyślij **71**. Postman zachowa cookie `4m_google_challenge` w cookie jar
   i zapisze publiczne clientId, nonce oraz termin ważności. Nie wyłączaj cookie jar.
2. Na stronie testowej z Google Identity Services pod dozwolonym JavaScript
   originem użyj dokładnie tego clientId i nonce przy generowaniu credential.
   Kolekcja nie otwiera okna Google i nie pobiera tokenu automatycznie. Wklej
   credential lokalnie do googleIdToken; nie eksportuj tokenów ani cookies.
   Sam ID token z innej próby nie wystarczy: ma inne nonce.
3. Wyślij **72** z zachowanym cookie. 200 zapisuje fourMoneyAccessToken
   i fourMoneyRefreshToken. 409 z google_registration_required pozwala przejść
   do **73** z tym samym tokenem i cookie przed upływem pięciu minut.
4. Dla **73** uzupełnij userLogin (bez @), userName, userSurname i userBirthDate.
   E-mail pochodzi z Google. Przy authentication=null potwierdź adres tokenem
   z SMTP przez folder 10, potem pobierz nowy challenge i zaloguj się Google.
   Po 503 email_delivery_failed konto już istnieje; użyj resend-confirmation.

Nie zmieniaj baseUrl/hosta ani HTTP/HTTPS między challenge a wymianą tokenu.
Jeśli challenge wykonujesz w przeglądarce, Postman nie ma jej cookie: należy
przenieść odpowiadające cookie lokalnie do cookie jar albo całą wymianę wykonać
w tej samej przeglądarce. Nie zapisuj browser tokenu w body ani repozytorium.
401 wymaga ponownego challenge i ID tokenu. Przy konflikcie wykrytym podczas
zapisu bazy challenge też może być już zużyty.

Profil zawiera hasPassword/isGoogleLinked. Dla konta Google bez hasła folder
resetu hasła (forgot-password → reset-password) ustawia pierwsze hasło przez SMTP.
Po ustawieniu hasła wymagane jest ponowne logowanie; Google nadal działa.
Zmiana maila zwraca 403, niezależnie od hasła. Odłączania Google nie ma.
Szczegółowy kontrakt i kody błędów:
[development](../agent-context/development.md#rejestracja-i-logowanie-google).
## Moduł Reports

Importuj `collections/4Money.Reports.postman_collection.json` i wspólne
środowisko 4Money.Local. Zaloguj się przez kolekcję User; Reports korzysta z
`fourMoneyAccessToken`. Do syntetycznej grupy domyślnej użyj konta mającego do
niej dostęp (np. seed_anna). Kolekcja zawiera wyłącznie odczyty i zatrzymuje Runner
po każdym żądaniu. Zapisane odpowiedzi są przykładami syntetycznymi, nie wynikami
wykonania żądań.

Parametry przykładów są zmiennymi kolekcji: `reportDateFrom`, `reportDateTo`,
`reportGroupId`, `reportCategoryId1`, `reportCategoryId2`. Można je nadpisać
w środowisku. UUID-y są syntetyczne, zgodne z lokalnym seedem.

Trasy GET: `category-breakdown`, `period-category-totals`,
`day-of-week-totals`, `daily-totals` pod `/api/reports`. Każda ma wariant
personal z jedną kategorią, group z dwiema kategoriami i pusty wybór.
Kategorie przesyłamy jako powtarzany parametr:
`categoryIds=UUID1&categoryIds=UUID2`. Pusty wybór oznacza pominięcie
parametru `categoryIds` i zwraca 200 z pustym raportem. Standardowe wiązanie
ASP.NET Core dla kolekcji Guid odrzuca `categoryIds=`, błędny UUID oraz
mieszanie pustych wartości z UUID (400).
Pusta lista nigdy nie wybiera wszystkich kategorii.

Wszystkie cztery raporty wymagają `dateFrom >= 1970-01-01`,
`dateTo >= 1970-01-01` i `dateFrom <= dateTo`; naruszenie zwraca 400.
Nie ma limitu długości zakresu, ograniczeń samego roku ani biznesowej górnej
granicy dat (pozostaje zakres .NET DateOnly). Do ręcznej kontroli każdej trasy
ustaw `reportDateFrom`/`reportDateTo`: `1969-12-31`/`1970-01-01` (400),
`1970-01-01`/`1969-12-31` (400), `1970-01-01`/`1970-01-01` (200),
`1970-01-01`/`2060-12-31` (200), `2026-11-30`/`2026-11-01` (400).

Przykłady okresowe pokazują progi 24/25/48/49 miesięcy kalendarzowych, również
niepełnych: Month do 24, Quarter do 48, dalej Year. Nie dodawaj granularity
do żądania. categoryId=null oznacza „Pozostałe”, niezależnie od wyświetlanej nazwy.
Średnie dni tygodnia obejmują także dni bez wydatków; kalendarz ma wszystkie
daty zakresu z zerami. Kontrakt i modele: [Reports API](../notes/REPORTS_API.md).

Weryfikacja 2026-11-17: ReportsProductionHostTests wykonał wszystkie 17 definicji
GET tej kolekcji przez HttpClient do rzeczywistego hosta API/Kestrel z PostgreSQL 15
i JWT uzyskanym przez login. Potwierdzono także generowanie OpenAPI i odrzucenie
sesji po logout. Nie uruchamiano Postman CLI ani zapisanych skryptów kolekcji;
odpowiedzi zapisane w JSON pozostają syntetycznymi przykładami kontraktu.

## Moduł Notifications


1. Zaimportuj kolekcję Notifications oraz aktualny szablon `4Money.Local`.
   Jeśli używasz już lokalnego środowiska, dodaj `notificationPageNumber=1`,
   `notificationPageSize=20` i puste `notificationId` bez nadpisywania swoich tokenów.
2. Zaloguj się przez kolekcję User w tym samym środowisku. Notifications korzysta
   z `baseUrl` i `fourMoneyAccessToken`; backend musi mieć zastosowaną migrację
   `AddNotificationsAfterRebase`. Każde żądanie dotyczy użytkownika z JWT.
3. Pobierz wszystkie, nieodczytane lub odczytane powiadomienia. Zmieniaj numer
   strony i rozmiar (1–100); sortowanie jest zawsze od najnowszych. Pusta lista
   jest poprawnym wynikiem. Samo pobranie nie oznacza odczytu.
4. Skopiuj wybrane `id` z `items` do lokalnego `notificationId` i wyślij oznaczenie
   pojedynczego powiadomienia. Zwraca 204 również przy ponowieniu, bez zmiany
   pierwszej daty odczytu. Cudze lub nieistniejące ID zwraca 404 ProblemDetails.
5. Oznaczenie wszystkich zmienia cały inbox konta, również poza widoczną stroną.
   Wyślij je ręcznie na koncie testowym. Każde żądanie zatrzymuje Runner,
   więc uruchomienie całej kolekcji nie wykonuje automatycznie kolejnych operacji.

Kolekcja sprawdza statusy sukcesu, paginację, DTO i filtr odczytu. Brak tokenu,
niepoprawna paginacja lub brak poprawnego UUID dla pojedynczej aktualizacji
pomijają żądanie. Oczekiwane błędy 401/404 podczas prób negatywnych dadzą czerwony
test sukcesu; sprawdź wtedy ProblemDetails. Nie ma publicznego endpointu NotifyAsync;
do niepustego inboxa potrzebne są wpisy odbiorców z lokalnego seeda albo
wewnętrznego wywołania serwisu. Samo utworzenie konta nie generuje powiadomień.
Aktualny seed ustawia wspólne hasło testowe `TestPassword1!` dla kont seedowych.
Do logowania użyj `seed_anna`, `seed_bartek` lub `seed_celina`; `seed_dawid` jest
celowo nieaktywny. W środowisku User ustaw odpowiednio userLogin i userPassword.
Zmiana pliku seed nie aktualizuje istniejącej bazy. Nie uruchamiaj go wyłącznie
w celu zmiany hasła: zawiera TRUNCATE i usuwa dotychczasowe dane.

Inbox obsługuje także `SettlementReceived` (`type=5`): utworzenie rozliczenia
w Grouping powiadamia odbiorcę pieniędzy, jeśli nie jest autorem wpisu.
Powiadomienie wskazuje grupę. Edycja i usunięcie rozliczenia nie tworzą powiadomień.
Rozszerzenie CHECK typów należy do `20261102215301_AddGroupingModel`;
baza z wcześniej zastosowaną wersją tej migracji nie zmieni się od samej edycji pliku.

## Banking i diagnostyka Tink

Folder **10 — Rozpoczęcie połączenia** zaczyna się od żądania
**10 — Zaloguj użytkownika 4Money**. Ustaw `baseUrl`, `userLogin` i `userPassword`
w wybranym środowisku. Login powinien zwrócić 200; skrypt zapisuje wyłącznie
`accessToken` jako `fourMoneyAccessToken`, dziedziczony przez kolejne żądania.
Refresh token nie jest zapisywany. Przed logowaniem czyszczone są stare tokeny;
brak konfiguracji, błąd HTTP lub wadliwa odpowiedź zatrzymują run. Po sukcesie
Runner przechodzi do 11, potem 12 i zatrzymuje się przed przeglądarką.

Do ponownego logowania przed folderem 20 lub 30 uruchom samo żądanie 10,
aby zachować zmienne istniejącej próby. Każdy login zastępuje wcześniejszą sesję
tego konta. Nadal można użyć osobnej kolekcji User do logowania i odświeżania.

Testy skryptu logowania na syntetycznych odpowiedziach, bez wywołań API:

```bash
node tools/scripts/tests/test_banking_collection_login.cjs
```

Uruchamiaj foldery etapami. Pełny run zatrzymuje się przed ręcznym krokiem
bankowym; nie oznacza zaliczenia całego flow. Chronione żądania 4Money wymagają
rzeczywistego auth. Moduł User implementuje login i może dostarczyć
`fourMoneyAccessToken` do żądań Banking. Diagnostyka Tink
nie zastępuje testu całego backendu.

Instrukcja zespołowa: [notes/TINK_POSTMAN.md](../notes/TINK_POSTMAN.md).

Lokalne eksporty nazywaj `*.local.postman_environment.json`. Katalog `results/`
i takie eksporty są ignorowane przez Git. Nie zapisuj odpowiedzi z sekretami jako
przykładów w kolekcji. Nie udostępniaj wartości tokenów, kodów ani Link URL-i.

## Refresh on demand

Automatyczny folder 40 i scenariusze 50–76 kolekcji Banking opisuje
[instrukcja symulacji refreshu](simulations/banking-refresh/README.md).
Obejmuje Runner, lokalną atrapę Tink, HTTPS, restart workera i kontrolę SQL.
