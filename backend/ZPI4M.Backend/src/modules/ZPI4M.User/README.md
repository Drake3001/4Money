# User — konto, sesje i integracje

Bieżący kontrakt konta i procedury konfiguracji. Kontrolery są w
[ApiControllers](ApiControllers/), DTO w [DTOs](DTOs/), przepływy w [Services](Services/).
Uruchamianie hosta: [development](../../../../../agent-context/development.md).
Przykłady są syntetyczne. Sekrety, tokeny i rzeczywiste dane użytkowników pozostają
wyłącznie w lokalnym środowisku, nigdy w eksporcie kolekcji lub logach.

## Wspólne zasady API

Ścieżki poniżej są względne wobec `/api/users`. Operacje profilu wymagają Bearer;
aktor pochodzi z ICurrentUser, klient nie wybiera konta do edycji. Odpowiedzi
prywatne i uwierzytelniające mają no-store. Błędy biznesowe to ProblemDetails,
walidacja DTO to ValidationProblemDetails; wybrane błędy Google mają dodatkowe
code. Sukcesy 202 i 204 nie mają body. 202 przy żądaniu wiadomości nie gwarantuje
wysłania ani dostarczenia maila.

Login ma 3–100 znaków, bez `@`, jest przycinany i normalizowany do małych liter.
E-mail ma maksymalnie 254 znaki i jest normalizowany. Login pozostaje unikalny
również po dezaktywacji; e-mail jest unikalny tylko dla aktywnych kont.
Hasło lokalne ma 8–128 znaków, wielką literę, cyfrę i znak specjalny.
Baza przechowuje hash przez IPasswordHasher, nie jawne hasło.

Tokeny mailowe to 32 losowe bajty w Base64Url (43 znaki); baza zachowuje SHA-256,
cel, TargetEmail i termin. Jednorazowe zużycie jest warunkowe, pod blokadą
użytkownika. Nowe żądanie po cooldownie zastępuje poprzedni token tego celu.
Żądanie nie zmienia jeszcze adresu/hasła/aktywności. Samo GET linku nie mutuje
konta; frontend interpretuje token i wykonuje odpowiedni POST.

## Rejestracja i potwierdzenie adresu

| POST | Dane | Wynik |
| --- | --- | --- |
| `/register` | login, name, surname, email, password, birthDate (`YYYY-MM-DD`) | 201 RegisterUserResponse; 400 dane, 409 login/aktywny e-mail |
| `/confirm-email` | token | 204; 400 token błędny, wygasły, zastąpiony lub zużyty |
| `/resend-confirmation` | email | 202 także dla nieznanego/potwierdzonego/nieaktywnego konta i cooldownu |

RegisterUserResponse zawiera id, login, name, surname, email, birthDate,
isEmailConfirmed i createdAt, bez hasła, hashy i tokenów. Trigger PostgreSQL tworzy
zwykłą grupę „Wydatki osobiste” w PLN oraz aktywnego członka-administratora w tej
samej transakcji INSERT. Grupa nie trafia do odpowiedzi rejestracji; po logowaniu
jest dostępna przez Grouping. Trigger nie uzupełnia starszych kont.

Zwykła rejestracja wysyła SMTP przed commitem. Błąd daje 503 i wycofuje konto,
token, grupę i członkostwo; można ponowić tym samym adresem. Awaria ponownej
wysyłki zachowuje poprzedni token i daty. Nie ma automatycznych ponowień.
Confirm zużywa istniejący token; nie wysyła nowego maila i nie odnawia ważności.

EmailConfirmationOptions ma domyślnie 24 h i 60 s cooldownu, ale host rejestruje
je bez Bind i walidacji opcji: JSON/środowisko obecnie nie nadpisują tych wartości.
Pozostałe opcje tokenów opisane dalej są bindowane i walidowane przy starcie.

## Logowanie i sesja

`POST /login` przyjmuje `{ "login": "anna@example.test", "password": "HASLO" }`.
Pole login przyjmuje login albo aktualny e-mail (3–254 znaków). Wartość z `@`
jest wyszukiwana wyłącznie wśród aktywnych kont po e-mailu, inna po loginie.
Serwis po zablokowaniu rekordu ponownie sprawdza zgodność identyfikatora.
Nieznane konto, błędne hasło i niepotwierdzony e-mail dają ogólne 401.
Dezaktywowany login z poprawnym hasłem daje 403 `Account is deactivated.`;
jego e-mail bez aktywnego właściciela daje 401. Sukces aktualizuje LastLoginTime
i w razie potrzeby odświeża hash hasła.

`POST /refresh` przyjmuje `{ "refreshToken": "TOKEN" }`; rotuje refresh token,
nie zmienia ID sesji i nie przedłuża jej bezwzględnego terminu. Stary refresh
daje 401. Obie operacje zwracają 200 z accessToken, accessTokenExpiresAt,
refreshToken, refreshTokenExpiresAt i tokenType=Bearer, daty UTC.
`POST /logout` wymaga ważnego Bearer, nie ma body i zwraca 204 po usunięciu sesji.
Z wygasłym JWT najpierw odśwież tokeny, aby wywołać autoryzowany logout.

JWT zawiera sub, sid i jti. Walidacja sprawdza HS256, issuer, audience, czas
bez tolerancji oraz aktywne, potwierdzone konto i aktualną niewygasłą sesję w bazie.
Jedna sesja na konto wynika z UNIQUE użytkownik/purpose. Nowy login zastępuje
sesję i unieważnia wcześniejsze JWT; zwykły refresh zachowuje sid, więc wcześniejszy
access token tej sesji działa do wygaśnięcia. Logout, reset hasła, zmiana e-maila
i dezaktywacja unieważniają także JWT przy kolejnym uwierzytelnieniu.

Login/refresh/logout blokują użytkownika, następnie token w jednej transakcji.
Równoległe użycie refresh dopuszcza jeden sukces. Usługi sesji nie zatwierdzają
transakcji wywołującego. Nie loguj tokenów przy diagnozie rotacji lub wylogowania.

W lokalnym środowisku ustaw `UserAuthentication__SigningKey` o co najmniej
32 znakach, kryptograficznie losowy, poza Git. Domyślne opcje Authentication:
Issuer=4Money, Audience=4Money.Client, AccessTokenLifetimeMinutes=15,
RefreshTokenLifetimeDays=7. Opcje są pod sekcją UserAuthentication; błędny klucz
zatrzymuje host. Seed tworzy syntetyczne konta opisane w SQL; nie jest potrzebny
do zwykłego logowania istniejącego użytkownika i usuwa dotychczasowe sesje.

## Rejestracja i logowanie Google

Integracja domyślnie jest wyłączona. API przyjmuje ID token Google Identity
Services, nie access token ani kod OAuth. W środowisku API/Compose:

```dotenv
GOOGLEAUTHENTICATION__ENABLED=true
GOOGLEAUTHENTICATION__CLIENTID=REPLACE_WITH_WEB_CLIENT_ID.apps.googleusercontent.com
```

Client ID to publiczny identyfikator Web application, nie sekret. Google Cloud
musi dopuszczać rzeczywiste JavaScript origins frontendu (schemat, host, port).
Nie potrzeba client secret ani callbacku OAuth API. Frontend używa ID z challenge,
bez drugiej zmiennej VITE. Włączona integracja wymaga poprawnego ClientId.

1. `POST /google/challenge`, bez body: 200 z clientId, nonce, expiresAt i cookie
   `4m_google_challenge` (HttpOnly, SameSite=Strict, Path=/api/users/google,
   Secure przy HTTPS). Nonce przekaż GIS, cookie wysyłaj automatycznie do API.
2. `POST /google/login` z idToken: podpis RS256, issuer, audience, termin, sub,
   e-mail i nonce są walidowane. Sukces 200 ma ten sam kontrakt sesji co hasło.
   Brak konta po sub daje 409 `google_registration_required`; challenge można
   wykorzystać do rejestracji z tym samym ID tokenem przed wygaśnięciem.
3. `POST /google/register`: idToken, login, name, surname, birthDate. E-mail i
   GoogleSubject pochodzą wyłącznie ze zweryfikowanego tokenu. 201 zawiera user,
   authentication albo null i requiresEmailConfirmation. `email_verified=true`
   oraz Gmail lub niepuste hd Workspace pozwalają potwierdzić adres i wydać sesję.
   Inny adres wymaga własnego SMTP, confirm-email i nowego logowania Google.

Konto jest wyszukiwane wyłącznie po GoogleSubject, nie dopasowywane po e-mailu.
Zmiana adresu w Google nie nadpisuje lokalnego adresu. Nie ma łączenia kont ani
odłączania Google. Konto bez hasła może ustawić pierwsze przez reset mailowy;
nie umożliwia to zmiany e-maila konta Google.

| Sytuacja | Odpowiedź / code |
| --- | --- |
| Błędny token, nonce/cookie, zużyty lub wygasły challenge | 401 invalid_google_credentials |
| Nieaktywne konto | 403 account_inactive |
| Niepotwierdzony adres przy loginie | 403 email_confirmation_required |
| Zajęty login/aktywny e-mail | 409 login_or_email_in_use |
| Zarejestrowany sub | 409 google_account_already_exists |
| Integracja wyłączona lub niedostępne metadane Google | 503 |
| Nieudany mail po rejestracji | 503 email_delivery_failed, konto już istnieje |

Challenge jest jednorazowo zużywany przed zapisem konta/sesji. Po błędzie zapisu
potrzeba nowego challenge i tokenu. Udany login/register usuwa cookie. Domyślne
opcje GoogleAuthentication: ChallengeLifetimeMinutes=5, MaxPendingChallenges=10000,
HttpTimeoutSeconds=10. Klucze/metadane Google są buforowane przez IdentityModel;
nieznany klucz zleca odświeżenie na kolejną próbę. Magazyn challenge jest lokalny
dla procesu: restart go kasuje, wiele instancji wymaga sticky sessions albo
przyszłego współdzielonego magazynu.

Przy innym origin fetch wymaga credentials=include i jawnego CORS z credentials;
host obecnie nie konfiguruje CORS. Najprostszy lokalny przepływ używa wspólnego
origin przez Vite proxy. SameSite=Strict wymaga wspólnego site; mieszanie HTTP
z HTTPS może je naruszyć. Reverse proxy musi poprawnie przekazać zaufane HTTPS.
Cookie nie jest polem JSON i nie zastępuje go przesłanie tokenu przeglądarki.

Rejestracja Google zapisuje konto, grupę i ewentualną sesję atomowo. Jeśli wymaga
potwierdzenia maila, wysyła SMTP po commicie: awaria zachowuje konto, grupę i
token, umożliwiając resend-confirmation. To wyjątek od wysyłki przed commitem w
pozostałych przepływach. Błędne dane dają 400, limity IP 429, wszystkie endpointy
mają no-store. Ręczne cookie/tokeny: [Postman](../../../../../postman/README.md#google).

## Profil

`GET /profile` zwraca id, login, name, surname, email, phone, birthDate, icon,
iconStorageKey, isEmailConfirmed, createdAt, lastLoginTime, hasPassword i
isGoogleLinked. Nie ujawnia hashy, tokenów ani identyfikatorów Banking.
`PUT /profile` przyjmuje pełne login/name/surname/birthDate i opcjonalny phone
oraz instrukcję zdjęcia. Nie edytuje e-maila, hasła ani flag konta.

Imię i nazwisko są wymagane, przycinane, do 100 znaków. BirthDate nie może być
MinValue ani przyszła według UTC. Telefon do 30 znaków podlega Phone; pominięcie,
null lub pusty tekst go czyści. Pominięcie/null/pusty icon **zachowuje** zdjęcie.
Sukces daje 200 z profilem, błędne dane 400, niedostępna sesja 401, zajęty login
409. Własny login jest dozwolony. Zmiana loginu zachowuje sesję. Serwis blokuje
konto i aktualizuje tylko pola profilu; konflikt indeksu obejmuje wyścig żądań.

## Zdjęcia profilu i prefiksy storage

`GET /profile/icon-presets` wymaga Bearer i zwraca posortowane `{ key, url,
expiresAt }`, także 200 z pustą tablicą. Presety PNG/JPG/JPEG/WebP trzeba wcześniej
umieścić w bucketcie. `GET /profile/icon` zwraca podpisany `{ url, expiresAt }`,
404 przy braku wyboru/niedostępnym presecie, 503 przy awarii storage. Własny URL
jest podpisywany bez HEAD, więc nie potwierdza istnienia pliku. Domyślna ważność
to 15 minut. W bazie zostaje klucz, nigdy podpisany URL.

| Wybór w PUT profilu | JSON | Multipart |
| --- | --- | --- |
| Zachowaj | Pomiń icon/removeIcon | Pomiń pola zdjęcia |
| Preset | icon=key | icon=key |
| Własny upload | Niedostępny | iconFile |
| Usuń | removeIcon=true | removeIcon=true |

Instrukcje plik/preset/usunięcie są wzajemnie wyłączne. Klient nie może ustawić
iconStorageKey ani wewnętrznego contentu przez JSON. W multipart pełne dane
profilu nadal są wymagane; klient sam tworzy Content-Type z boundary.
Limit żądania to 6 MiB, pliku 5 MiB; pusty daje 400, za duży 413, niedozwolony
format/sygnatura lub wadliwy obraz 415. MIME: PNG, JPEG, WebP, HEIC/HEIF.

Shared.Images.IconImageProcessor odczytuje pierwszą klatkę przez Magick.NET,
sprawdza do 16384 px na bok i 64 mln pikseli, stosuje orientację EXIF/sRGB,
zmniejsza do 512×512 bez powiększania, usuwa metadane i zapisuje WebP jakości 85.
Frontend przyjmuje źródło do 25 MiB, ale wysyła przygotowany obraz do 5 MiB;
bezpośredni upload do API nadal ma limit 5 MiB. Nie przetwarza to dawnych plików
ani presetów. Użytkownik ma wyłącznie własny prefiks `users/{userId}/icons/{guid}`.

Icon i IconStorageKey nie mogą być niepuste jednocześnie (CHECK). Nowy plik przy
błędzie bazy usuwa się dopiero po potwierdzonym rollbacku; zastąpiony własny plik
po potwierdzonym commicie. Presetów nie usuwamy. Błąd cleanupu nie cofa profilu,
a niepewny commit zachowuje pliki. Brak wspólnej transakcji S3/PostgreSQL i workera
uzgadniania. Grouping używa wspólnego procesora, ale ma własny cykl życia pliku.

```dotenv
USER_ICON_PRESET_PREFIX=icons/presets/users/
GROUP_ICON_PRESET_PREFIX=icons/presets/groups/
RECEIPT_STORAGE_PREFIX=receipts/
```

Prefiksy są w jednym STORAGE_BUCKET, nie oznaczają osobnych bucketów. Presety
kończ `/`; Receipt normalizuje ukośnik. Zmiana prefiksu Receipt dotyczy nowych
plików, stare odczytuje zapisanymi kluczami. Zmiana katalogu presetów zmienia
listę wyborów. Po zmianie opcji restartuj API/odtwórz kontener backendu.

## Zmiana e-maila, reset i dezaktywacja

| POST | Warunek i dane | Wynik |
| --- | --- | --- |
| `/profile/change-email` | Bearer; newEmail, currentPassword | 202; adres/sesja bez zmian do potwierdzenia |
| `/confirm-email-change` | Anonimowo; token | 204, nowy e-mail i usunięcie wszystkich tokenów |
| `/forgot-password` | Anonimowo; email | Ogólne 202 także dla nieznanego konta, cooldownu lub błędu SMTP |
| `/reset-password` | Anonimowo; token, newPassword | 204, zmiana hasła i usunięcie wszystkich tokenów |
| `/profile/deactivate` | Bearer; bez body i bez hasła | 202; konto nadal aktywne do potwierdzenia |
| `/confirm-deactivation` | Anonimowo; token | 204, IsActive=false i usunięcie wszystkich tokenów |

Zmiana e-maila wymaga aktywnego, potwierdzonego konta i poprawnego aktualnego
hasła. Błędne hasło/ten sam adres daje 400, brak sesji 401, zajęty adres 409,
cooldown 429, SMTP 503. Nowy adres nie jest rezerwowany: przy potwierdzeniu
unikalność jest sprawdzana ponownie. Konflikt 409 cofa także zużycie tokenu.
Konta Google zawsze dostają 403 google_account_email_change_forbidden, także po
ustawieniu hasła; stary token zmiany adresu nie pozwala obejść blokady.

Forgot-password wystawia token tylko dla aktywnego, potwierdzonego konta.
Nie zmienia jeszcze hasła ani nie wylogowuje. Dla Google bez hasła ustawia pierwsze
lokalne hasło przez ten sam reset; hasło Google jest niezależne. 202 nie ujawnia
istnienia konta, ale czasy odpowiedzi nie są wyrównywane. SMTP działa w żądaniu,
a błąd jest logowany ogólnie bez adresu/tokenów/szczegółów SMTP. Reset sprawdza
TargetEmail zgodny z bieżącym adresem, blokuje konto, zużywa token, zmienia hash
i usuwa wszystkie tokeny w jednej transakcji. Błąd zapisu cofa także zużycie.

Dezaktywacja nie usuwa użytkownika ani danych finansowych i nie oferuje reaktywacji.
Anonimowe potwierdzenie wymaga tokenu właściwego celu i aktualnego adresu aktywnego,
potwierdzonego konta. Poprawne hasło nie zastępuje tokenu potwierdzenia.

Token nieprawidłowy, innego celu, zastąpiony, wygasły lub zużyty daje 400.
Po sukcesie każdego potwierdzenia zmiany adresu/resetu/dezaktywacji stare JWT i
refresh przestają działać. Błędne DTO nie zużywa tokenu. Wysyłka przed commitem
zachowuje poprzedni token przy awarii i nie rozpoczyna nowego cooldownu.

| Sekcja opcji | Ważność | Cooldown | Bindowanie/walidacja |
| --- | --- | --- | --- |
| EmailConfirmation | TokenLifetimeHours=24 | ResendCooldownSeconds=60 | Obecnie bez Bind |
| EmailChange | TokenLifetimeHours=24 | ResendCooldownSeconds=60 | Przy starcie |
| PasswordReset | TokenLifetimeMinutes=30 | ResendCooldownSeconds=60 | Przy starcie |
| AccountDeactivation | TokenLifetimeMinutes=30 | ResendCooldownSeconds=60 | Przy starcie |

## SMTP i linki frontendowe

FRONTEND_URL jest wymagany i walidowany przy starcie hosta: absolutne HTTP/HTTPS,
bez credentials/query/fragmentu, np. `http://localhost:5173`. Nadpisuje opcję
UserEmailLinks.FrontendBaseUrl; ustawienie samej tej opcji go nie zastępuje.
Link ma formę `/?action=confirm-email#token=...`; inne akcje to confirm-email-change,
confirm-deactivation i reset. Frontend usuwa token z URL po sukcesie. Reset
wymaga wpisania hasła; inne akcje wykonują POST po otwarciu strony. Do ręcznych
testów kopiuj i dekoduj token z fragmentu zamiast klikać link, który może go zużyć.

Wysyła wspólny IEmailSender/SmtpEmailSender oparty na MailKit. Szablony
UserEmailMessages tworzą HTML i tekst z zachowanym URL; logo jest załącznikiem
MIME CID, termin Europe/Warsaw. Każda wysyłka ma osobne połączenie SMTP.
Nie ma automatycznego retry ani gwarancji dostarczenia do skrzynki.

```dotenv
SMTP__HOST=SMTP_HOST
SMTP__PORT=587
SMTP__SECURITYMODE=StartTls
SMTP__USERNAME=SMTP_LOGIN
SMTP__PASSWORD=SMTP_PASSWORD
SMTP__SENDEREMAIL=sender@example.test
SMTP__SENDERNAME=4Money
SMTP__TIMEOUTSECONDS=30
SMTP__CHECKCERTIFICATEREVOCATION=true
```

To placeholdery do lokalnego .env. Login i hasło muszą być oba ustawione albo
oba puste. StartTls wymaga STARTTLS (zwykle 587), SslOnConnect TLS od początku
(zwykle 465), None jest do nieszyfrowanego środowiska developerskiego. Opcje
SMTP są sprawdzane przy wysyłce, nie ValidateOnStart. Sam start API nie dowodzi
poprawnych danych skrzynki. Przy lokalnym braku CRL/OCSP tymczasowe false dla
CheckCertificateRevocation wyłącza tylko sprawdzanie odwołania, nie TLS/nazwy
serwera/zaufania; produkcja wymaga działającej kontroli odwołania.

## Limity i weryfikacja

Stałe okna po 60 s, bez kolejki, na RemoteIpAddress (brak adresu: unknown),
lokalne dla instancji. Nie czytamy ręcznie nagłówków proxy. Każde dopuszczone
żądanie liczy się również przy błędnym DTO. Przekroczenie daje 429.

| Polityka | Domyślnie / min / IP | Użycie |
| --- | --- | --- |
| user-login | 30 | Login hasłem, Google challenge/login |
| user-refresh | 120 | Refresh |
| user-password-reset | 10 | Reset-password |
| user-email | 5 | Rejestracja, Google register, resend, operacje zmiany/potwierdzeń mailowych, forgot, dezaktywacja |

UserRateLimiting__LoginPermitLimit, RefreshPermitLimit i PasswordResetPermitLimit
muszą być dodatnie; błędne opcje zatrzymują start. Cooldown na konto jest osobną
kontrolą w bazie, nie częścią limitu IP. Wspólne IP dzieli limit danej polityki,
a wiele instancji nie współdzieli liczników.

[Kolekcja User](../../../../../postman/README.md) wymaga ręcznego dostarczenia
tokenów z maila, nie jest automatycznym e2e. Po zmianie profilu sprawdź GET,
konflikt loginu, błędne pola i brak sesji. Po reset/change/deactivate sprawdź
odrzucenie starych JWT/refresh, jednorazowość tokenu i rollback awarii. Google
wymaga oddzielnie testu cookie/nonce, pierwszego hasła i błędu SMTP po commicie.
Automatyczne testy: [instrukcja](../../../tests/README.md). Nie uruchamiaj ich
na bazie użytkownika; prawdziwe SMTP/Google/storage wymagają osobnego zakresu.
