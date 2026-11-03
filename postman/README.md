# Postman — 4Money i diagnostyka Tink

Kolekcje User i Notifications odpowiadają obecnej implementacji tych modułów.
Kolekcje Banking i Tink obejmują połączenia bankowe oraz diagnostykę dostawcy;
ich obecność nie potwierdza wykonania rzeczywistego przepływu Sandbox.

| Kolekcja | Środowisko | Przeznaczenie |
| --- | --- | --- |
| `collections/4Money.User.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Rejestracja, potwierdzenia e-maila, login/refresh/logout, profil, zmiana e-maila, reset hasła i dezaktywacja |
| `collections/4Money.Notifications.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Inbox z paginacją, wszystkie/nieodczytane/odczytane, oznaczanie jednego lub wszystkich jako odczytane |
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
   JWT i bazy z migracją `AddAccountDeactivationTokens`.
2. Uzupełnij lokalnie `userLogin`, `userEmail` (dostępna skrzynka), `userPassword`
   i dane profilu. Hasło ma mieć 8–128 znaków, wielką literę, cyfrę i znak specjalny.
   `userBirthDate` ma format `YYYY-MM-DD`. Nie eksportuj prawdziwych danych do Git.
3. Uruchom rejestrację przez **Send**. Token z maila wklej do
   `userEmailConfirmationToken`, wyślij potwierdzenie, potem login.
   Login zapisuje `fourMoneyAccessToken` i `fourMoneyRefreshToken` w środowisku.
   Żądania chronione pobierają Bearer automatycznie; refresh token trafia do body.
4. Pobierz lub edytuj profil. PUT wysyła komplet pól. Puste `userPhone` i `userIcon`
   czyszczą odpowiadające im pola. Logout wykonaj po operacjach chronionych
   albo zaloguj się ponownie przed kolejnym chronionym żądaniem.
5. Dla zmiany e-maila ustaw `userNewEmail`, a token z nowej skrzynki wklej do
   `userEmailChangeToken`. Potwierdzenie usuwa sesję i aktualizuje lokalne `userEmail`.
   Dla resetu hasła ustaw `userNewPassword`, poproś o reset i wklej token do
   `userPasswordResetToken`. Sukces aktualizuje lokalne `userPassword` i usuwa sesję.
   Po tych potwierdzeniach zaloguj się ponownie.
6. Dezaktywację wykonuj na końcu na koncie testowym. Żądanie wymaga Bearer,
   bez body; konto nadal działa. Token z maila wklej do `userDeactivationToken`
   i ręcznie wyślij potwierdzenie. Sukces ustawia `is_active=false` i usuwa wszystkie
   tokeny. Kolejny login poprawnym hasłem zwraca 403. Nie ma endpointu reaktywacji.

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
