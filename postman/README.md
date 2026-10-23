# Postman — 4Money i diagnostyka Tink

Kolekcja User odpowiada obecnej implementacji modułu użytkownika.
Kolekcje Banking i Tink dotyczą osobnego przepływu integracji bankowej;
ich obecność nie potwierdza implementacji tych endpointów w bieżącej gałęzi.

| Kolekcja | Środowisko | Przeznaczenie |
| --- | --- | --- |
| `4Money.User.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Rejestracja, potwierdzenia e-maila, login/refresh/logout, profil, zmiana e-maila, reset hasła i dezaktywacja |
| `4Money.Banking.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Rozpoczęcie próby, odczyt rezultatu, połączeń, zgód i błędy callbacku |
| `Tink.Diagnostics.postman_collection.json` | `environments/Tink.Sandbox.example.postman_environment.json` | Token aplikacji, token użytkownika, zgody oraz osobne delegowanie Tink Link |

Importuj kolekcję i właściwy szablon środowiska. Wartości poufne uzupełniaj
wyłącznie lokalnie. Kolekcje używają formatu v2.1 oraz aktualnego Postman Desktop
z obsługą `pm.execution.skipRequest`, `pm.execution.setNextRequest` i `URL`.

## Moduł User

1. Zaimportuj kolekcję User i szablon środowiska `4Money.Local`, a następnie
   wybierz to środowisko. `baseUrl` nie zawiera `/api`, np. `https://localhost:7278`
   albo `http://localhost:5283`. Backend wymaga lokalnej konfiguracji SMTP,
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

Limity `user-email` i `user-auth` wynoszą po 5 żądań/minutę/IP. Cooldown wysyłek
na konto to domyślnie 60 s. Tokeny potwierdzenia i zmiany e-maila są ważne 24 h,
resetu i dezaktywacji 30 min. `forgot-password` zwraca 202 również bez wysyłki
(np. konto nieaktywne, cooldown lub awaria SMTP).

## Banking i diagnostyka Tink

Uruchamiaj foldery etapami. Pełny run zatrzymuje się przed ręcznym krokiem
bankowym; nie oznacza zaliczenia całego flow. Chronione żądania 4Money wymagają
rzeczywistego auth. Moduł User implementuje login i może dostarczyć
`fourMoneyAccessToken`, ale obecna gałąź nie zawiera kontrolerów Banking/Tink.
Opisy w starszej kolekcji Banking dotyczą wcześniejszego planu integracji;
nie traktuj ich jako opisu aktualnego stanu backendu. Diagnostyka Tink
nie zastępuje testu całego backendu.

Lokalne eksporty nazywaj `*.local.postman_environment.json`. Katalog `results/`
i takie eksporty są ignorowane przez Git. Nie zapisuj odpowiedzi z sekretami jako
przykładów w kolekcji. Nie udostępniaj wartości tokenów, kodów ani Link URL-i.
