# Podłączenie banku przez Tink

Jedynym wejściem użytkownika jest `POST /api/banking/connection-attempts`.
Zwraca 201 z `attemptId`, `authorizationUrl`, `expiresAt` i nagłówkiem Location.
Poprzedni endpoint `/api/integrations/tink/link` został usunięty.

## Warunki rzeczywistego uruchomienia

Przed włączeniem integracji potwierdź dla **tej samej aplikacji Tink**:

- zatwierdzony dostęp do Transactions Continuous Access i Permanent Users;
- obsługę wybranego rynku oraz Demo Bank dla tego produktu;
- wymagane uprawnienia aplikacji i delegowane scopes;
- zarejestrowany callback backendu i zgodność otrzymanego wyniku z kontraktem SDK.

Dokumentacja produktu jest potwierdzeniem kontraktu, nie uprawnień naszego konta.
`Tink:ContinuousAccessVerified` domyślnie jest false. Nie ustawiaj go na true
wyłącznie w celu obejścia błędu. Nie ma automatycznego fallbacku na Account Check,
One-Time Access ani PIS. W tej sesji nie wykonano rzeczywistych wywołań Tink.

Po potwierdzeniu warunków skonfiguruj środowisko lub ignorowany lokalny `.env`:

```dotenv
Tink__ClientId=<identyfikator aplikacji>
Tink__ClientSecret=<sekret aplikacji>
Tink__RedirectUri=https://<backend>/api/banking/tink/callback
Tink__ContinuousAccessVerified=true
Banking__AttemptLifetimeMinutes=15
```

Redirect musi dokładnie odpowiadać wpisowi w Console. Do testów lokalnych użyj
adresu dopuszczonego przez Tink. Callback zwraca JSON; frontend nie jest wymagany.
Działające logowanie 4Money jest osobnym warunkiem ręcznego testu startu. Nie ma
nagłówka, który pozwalałby ominąć brak uwierzytelniania w hoście produkcyjnym.

## Zweryfikowane kontrakty i implementacja

Sprawdzono 2026-10-21 w aktualnej publicznej dokumentacji Tink (treść artykułów
z oficjalnego CDN oraz referencja API publikowana przez docs.tink.com):

| Operacja | Kontrakt używany przez adapter |
| --- | --- |
| Token aplikacyjny | `POST /api/v1/oauth/token`, formularz `grant_type=client_credentials` |
| Tworzenie Permanent User | `POST /api/v1/user/create`, JSON z `external_user_id`, `market`, `locale`; scope `user:create` |
| Delegowanie Tink Link | `POST /api/v1/oauth/authorization-grant/delegate`, `user_id`, `actor_client_id`, `id_hint`, scopes |
| Token użytkownika | Osobny `POST /api/v1/oauth/authorization-grant`, następnie wymiana jego kodu przez OAuth token endpoint |
| Weryfikacja credential | `GET /api/v1/credentials/{id}` z tokenem tego użytkownika i `credentials:read` |
| Odzyskanie użytkownika | Grant po `external_user_id`, token z `user:read`, następnie `GET /api/v1/user` |
| Link | `https://link.tink.com/1.0/transactions/connect-accounts`, delegowany `authorization_code` i losowy `state` |
| Callback sukcesu | `credentials_id` oraz przekazany wcześniej `state` |
| Callback błędu | `error`, `error_reason`, opcjonalne dodatkowe parametry SDK |

Źródła:

- [Continuous access](https://docs.tink.com/resources/transactions/continuous-connect-to-a-bank-account)
- [Transactions SDK reference](https://docs.tink.com/resources/transactions/transactions-sdk-reference)
- [API: OAuth i User](https://docs.tink.com/api)
- [API: Credentials](https://docs.tink.com/api-connectivity-v1)
- [Transactions errors](https://docs.tink.com/resources/transactions/handle-transactions-error-codes)

Tokeny aplikacyjne są cache'owane według klienta, adresu API i dokładnego zestawu
scopes. Tworzenie użytkownika żąda `user:create`, a granty `authorization:grant`.
Token użytkownika do weryfikacji żąda tylko `credentials:read`; odzyskanie
mapowania żąda `user:read`. Delegowane scopes pozostają w konfiguracji Tink Link.
Nie dodajemy uprawnień płatniczych ani uprawnień do zapisu danych finansowych.

## Odporność i bezpieczeństwo

Istniejący `User.TinkUserId` jest wykorzystywany ponownie. Inicjalizację chroni
transakcyjna blokada doradcza PostgreSQL dla danego użytkownika, również między
instancjami backendu. Nie blokuje zapisów logowania ani profilu.
Nowy użytkownik otrzymuje stabilny `external_user_id` postaci `4money-<local-guid>`.
Tink dokumentuje 409 dla zduplikowanego identyfikatora zewnętrznego. Po konflikcie
lub niejednoznacznym wyniku utworzenia adapter próbuje odzyskać ten sam wpis
przez grant i odczyt użytkownika. Nie generuje nowego identyfikatora, nie usuwa
użytkownika w Tink i nie ponawia automatycznie POST tworzącego użytkownika.
Jeśli odzyskanie się nie powiedzie, próba kończy się bezpiecznym błędem; operator
może zweryfikować dostępność/uprawnienia przed kolejną próbą.

Próba jest zapisana przed kontaktem z Tink. Powrót z SDK nie oznacza zakończenia
importu. Callback potwierdza credential po stronie dostawcy; dla niegotowego stanu
zwraca 503 z Retry-After, pozostawiając próbę do ponowienia przed wygaśnięciem.
Po potwierdzeniu powstaje połączenie z synchronizacją NOT_STARTED.

Cache tokenu aplikacyjnego wygasa absolutnie z marginesem czasu. Tokenów
użytkownika, kodów i linków nie zapisujemy ani nie cache'ujemy. HTTP ma timeout
30 sekund, wyłączone przekierowania i loggery. Nie loguj query stringów callbacku,
pełnych URL-i, body żądań/odpowiedzi ani Authorization także na proxy. Logi
serwisu zawierają tylko etap, kategorię, status upstream i lokalny identyfikator
próby. Błędy Tink są mapowane na 503; lokalne odczyty działają bez Tink.

Pełny kontrakt, persystencję i ograniczenia opisuje
[README Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md).

## Lista zgód na żądanie

`GET /api/banking/consents` zwraca bieżące zgody zalogowanego użytkownika,
powiązane wyłącznie z jego lokalnymi połączeniami. Backend używa istniejącego
`TinkUserId`, pobiera grant ze scope’em `provider-consents:read`, wymienia kod
na User Access Token i wywołuje `GET /api/v1/provider-consents`. Wymagane jest
udostępnienie aplikacji scope’u `provider-consents:read` w Tink Console.
Nie dodajemy go do scope’ów Tink Link ani tokenu aplikacyjnego.

Przykładowa odpowiedź 4Money:

```json
[
  {
    "connectionId": "00000000-0000-0000-0000-000000000001",
    "providerStatus": "UPDATED",
    "sessionExpiresAt": "2026-12-01T12:00:00+00:00",
    "sessionExtendable": false
  }
]
```

Dwa ostatnie pola mogą być null. Status jest wartością raportowaną przez Tink,
a nie lokalnym statusem połączenia ani gwarancją ważności zgody. Odpowiedź nie
ujawnia identyfikatorów użytkownika, credentials ani rachunków w Tink.
Brak mapowania lub lokalnych połączeń daje pustą listę bez wywołania dostawcy.
Brak zgody w wyniku nie powoduje oznaczenia połączenia jako cofniętego.

Odczyt nie zmienia bazy, nie cache’uje wyników ani User Access Tokenów i nie
aktualizuje `ConsentExpiresAt`. Callback nie pobiera tej listy. Nadal obowiązują
rzeczywiste auth, warunki konfiguracji produktu i blokada `ContinuousAccessVerified`.
Błędy integracji dają bezpieczne 503, a odpowiedź ma `Cache-Control: no-store`.
To etap odczytu zgód, bez przedłużania zgód i bez synchronizacji danych bankowych.

Źródło zakresu uprawnień: [Tink API](https://docs.tink.com/api/statistics).

## Weryfikacja lokalna

```sh
dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --configuration Release
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Banking.UnitTests/ZPI4M.Banking.UnitTests.csproj --configuration Release --no-build
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Banking.Tests/ZPI4M.Banking.Tests.csproj --configuration Release --no-build
dotnet test backend/ZPI4M.Backend/tests/ZPI4M.Banking.IntegrationTests/ZPI4M.Banking.IntegrationTests.csproj --configuration Release --no-build
```

Dla testów integracyjnych ustaw `BANKING_TEST_POSTGRES` na connection string
**odizolowanego serwera testowego**, którego użytkownik może tworzyć bazy.
Fixture uruchamia SQL bazowy i migracje w nowej bazie `banking_tests_<guid>`,
a następnie usuwa wyłącznie tę bazę. Nigdy nie wskazuj środowiska produkcyjnego.
Bez zmiennej testy są jawnie pomijane. Testy używają atrapy Tink, nie potwierdzają
uprawnień Sandbox ani logowania do 4Money.
