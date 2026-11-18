# Domena i model danych

Czytaj przed zmianą modeli, wydatków, podziału kosztów, limitów oraz przetwarzania
paragonów. Zakres zachowań opisuje [produkt](product.md), a organizację kodu
[architektura](architecture.md).

Zakres MVP i opis tabel ponownie sprawdzono 2026-10-23 względem tekstów
`Funkcjonalności.txt` i `Baza danych.txt`, w tym purpose 5 i warunek TargetEmail
z aktualnego opisu bazy. Przegląd diagramów opisany niżej
zachowuje wcześniejszą datę dla niezmienionych źródeł. Opis bazy, modele
konceptualny i logiczny oraz podział encji ponownie sprawdzono 2026-10-27.
Opis bazy zawiera już NotificationRecipient, nowe pola Notification,
Receipt.storage_key i BankAccount.credentials_id oraz złożoną unikalność rachunku.

`Słownik pojęć.txt` ponownie sprawdzono 2026-10-27 po aktualizacji użytkownika.
Zawiera Produkt, Wydatek, Kategorię, Paragon, Konto, Grupę Wydatkową, Limit/Budżet
i Raport/Podsumowanie; usunięto definicję Prognozy oraz numerację pojęć.
Nie traktuj wcześniejszej definicji jako aktualnego wymagania prognozowania wydatków.

Zakres funkcjonalny ponownie porównano z `Funkcjonalności.txt` 2026-10-27:
podział kosztów jest teraz w 2.8 (MVP), limity w 2.6 i raportowanie w 2.9
nie mają oznaczenia MVP. Automatyczne dopasowanie paragonów do transakcji
oraz zwykły i interaktywny push są rozszerzeniami z sekcji 3. Inbox jest
jawnie częścią MVP (2.10).

## Pojęcia i encje

| Pojęcie / encja | Znaczenie i właściciel |
| --- | --- |
| Konto / `User` | Profil użytkownika aplikacji; moduł User. To nie rachunek bankowy. |
| `UserToken` | Token potwierdzenia lub zmiany e-maila, resetu hasła, odświeżania sesji albo dezaktywacji konta; User |
| Grupa wydatkowa / `Group` | Przestrzeń współdzielenia kosztów; Grouping |
| `GroupMember` | Członkostwo użytkownika w grupie, z datą dołączenia i czterema agregatami kwotowymi; Grouping |
| Wydatek / `Expense` | Zakup pochodzący z ręcznego wpisu, paragonu lub banku; Expense |
| Produkt / `ExpenseItem` | Pozycja zakupu: produkt, usługa lub towar, z kategorią; Expense |
| `Category` | Kategoria produktów, np. żywność, ubrania lub subskrypcje; Expense |
| `ExpenseShare` | Procentowy udział użytkownika w wydatku; kwota jest wyliczana przez Expense |
| Paragon / `Receipt` | Zdjęcie lub zrzut potwierdzenia zakupu; odnośnik do obrazu i surowy wynik OCR; Receipt |
| `BankAccount` | Rachunek bankowy przypisany użytkownikowi; Banking |
| `BankConnectionAttempt` | Trwała próba połączenia z bankiem, skrót state, termin i wynik; Banking |
| `BankConnection` | Lokalne połączenie z bankiem; stan połączenia odrębny od synchronizacji; Banking |
| `BankTransactionAssignment` | Rejestr wykorzystania transakcji przez Expense albo GroupSettlement; Banking; szczegóły w sekcji poniżej |
| `BankTransaction` | Transakcja zaimportowana z rachunku, odrębna od wydatku; Banking |
| Limit / `GroupLimit`, `CategoryGroupLimit` | Ogólny limit grupy na okres oraz zachowany próg kategorii; Limits. |
| `Notification` / `NotificationRecipient` | Treść powiadomienia z opcjonalnym FK grupy albo wydatku oraz odbiorcy z indywidualną datą odczytu; Notifications; migracja AddNotificationsAfterRebase |
| Raport | Agregacja danych, a nie osobna encja w obecnym projekcie bazy; Reports |

Dokumentacja opisuje zakup jako jeden lub wiele produktów; diagram i aktualny
model dopuszczają kolekcję bez pozycji. Ustalono zwykłe pozycje lub systemowe
„Nieznane”; ExpenseRequestService egzekwuje tę regułę przy tworzeniu i edycji.

## Relacje i reprezentacja

Reguły Expense i otwarte decyzje opisują [ustalenia zespołu](../notes/EXPENSE_DECISIONS.md).

`Category.Icon` przechowuje nazwę ikony Lucide jako wymagany `text`.
Kolor kategorii zależy od motywu frontendu i nie jest polem encji.
Migracja `CompleteExpenseModel` usuwa kolumnę `color`; `icon` był już typu `text`
w SQL init i snapshocie EF.

- `Expense` należy do grupy; `FkGroupId` jest wymaganym `Guid`. Powiązania
  z paragonem i transakcją bankową są opcjonalne i maksymalnie 1:1.
- `Expense.Version` jest wymaganym UUID i tokenem współbieżności EF; migracja
  `CompleteExpenseModel` dodaje kolumnę z domyślnym `gen_random_uuid()`. API zwraca tekstowy
  nieprzezroczysty token. PUT i DELETE wymagają aktualnej wersji; konflikt daje 409.
- `Expense` ma jednego wymaganego płatnika `FkPayerId`, bez osobnego autora.
  FK do User jest konfigurowany w AppDbContext z DeleteBehavior.NoAction.
- `ExpenseItem` należy do wydatku i kategorii, bez przypisania użytkownika.
  Zawiera cenę jednostkową (także ujemną lub zerową), dodatnią ilość, kwotowy
  `Discount <= 0` (domyślnie 0), opcjonalne tekstowe `Unit` i flagę
  `IsSystemGenerated` (domyślnie false) dla systemowego „Nieznane”.
- `ExpenseShare` łączy wydatek i użytkownika kluczem złożonym oraz procentem.
  MVP nie utrwala trybu podziału ani jego wejściowych kwot/proporcji.
  Nie jest zapisem wykonanego przelewu; płatności realizuje Grouping.
  Relacja do Expense jest mapowana w konfiguracji modułu, bez nawigacji.
  FK Expense do Group/Receipt/BankTransaction oraz ExpenseShare do User są
  wymuszane przez bazowy SQL, bez mapowania tych relacji w EF. AppDbContext
  mapuje osobno FK płatnika Expense do User oraz Notification do Expense.
- `GroupMember` łączy użytkownika i grupę kluczem złożonym; użytkownik może
  należeć do wielu grup.
- `BankAccount` należy do użytkownika, a `BankTransaction` do rachunku.
  Identyfikatory dostawcy występują w modelu bankowym, ale nie są automatycznie
  publicznym kontraktem API.
  Historyczny opis bazy wskazywał UNIQUE (fk_user_id, financial_institution_id,
  number). Bieżąca konfiguracja zachowuje opcjonalne credentials_id, ale
  tożsamość importu wyznacza unikalna para (TinkUserId, TinkAccountId).
  Numer, IBAN i instytucja są opcjonalne. Nie zakładaj wdrożenia bez migracji.
- `CategoryGroupLimit` łączy kategorię i grupę. Modele przewidują opcjonalne
  limity tygodniowe, miesięczne i roczne.

Aktualne konfiguracje EF używają m.in. `decimal(18,2)` dla kwot,
`decimal(18,3)` dla ilości oraz `decimal(5,2)` dla procentów udziałów.
Zachowuj reprezentację dziesiętną pieniędzy; nie zmieniaj precyzji bez analizy
modelu i migracji.

## Zatwierdzony model grup — 2026-10-28

Od `20261109191600_UserImprovements` każde nowo dodane konto
otrzymuje zwykłą grupę „Wydatki osobiste” z walutą PLN. Użytkownik jest jej
jedynym początkowym członkiem i administratorem, agregaty wynoszą zero.
Nazwa nie identyfikuje specjalnego rodzaju grupy: można ją zmienić, zaprosić
inne osoby i archiwizować grupę zgodnie ze zwykłymi regułami Grouping.
Istniejące konta nie dostają grup wstecznie. Implementację transakcyjną opisuje
[architektura](architecture.md#grupa-startowa-nowego-konta).

Migracja `ApprovedGroupingModel` zmienia `grouping.group._id` na `id`, usuwa
trzy limity grupy i dodaje wymagane `AdminUserId` / `admin_user_id` oraz
`CurrencyCode` / `currency_code` (dokładnie trzy wielkie litery ASCII).
Administrator pochodzi wyłącznie z grupy; `GroupMember.IsAdmin` znika.
Klasa C# `GroupMember` pozostaje, tabela to `grouping.group_user`, klucz
`(user_id, group_id)`. Od 2026-10-30 `IsMember` / `is_member` jest wymaganym
boolean z domyślnym true (również w nowej encji C#); false oznacza opuszczenie
grupy przy zachowaniu wpisu i historycznych agregatów. Pole dodano na życzenie
użytkownika do istniejącej migracji ApprovedGroupingModel, designera i snapshotu.
Sam model nie implementuje operacji opuszczenia grupy ani filtrowania odczytów.
`joined_at` ma typ timestamptz i jest przekazywane jawnie przy zapisie.
`total_paid`, `total_share`, `total_settlements_sent`, `total_settlements_received`
są nieujemnymi numeric(18,2) z domyślnym zerem.

Expense zachowuje nazwy `_id`, `fk_group_id`, `fk_payer_id`, `name`,
`transaction_time`, `fk_bank_transaction_id` i `fk_receipt_id`.
`expense.expense_share` ma klucz `(fk_expense_id, fk_user_id)` i `percentage`
numeric(5,2), zgodnie z modelem gałęzi Expense. Kwoty do kontraktu agregatów
Grouping są wyliczane przez Expense; tworzenie aktualizuje agregaty atomowo
z wydatkiem od 2026-11-03.

`grouping.group_settlements` / `GroupSettlement`: id, group_id, from_user_id,
to_user_id, dodatnia amount numeric(18,2), method (Cash=1, BankTransfer=2,
Other=3), wymagane settled_at UTC i opcjonalne unikalne bank_transaction_id.
Nadawca musi różnić się od odbiorcy. Jest to zapis rozliczenia wykonanego poza
aplikacją, nie zlecenie przelewu.

`grouping.group_invitation` / `GroupInvitation`: id, group_id, created_by_user_id,
unikalny token_hash, max_uses > 0, use_count od 0 do max_uses (domyślnie 0),
wymagane expires_at UTC oraz is_active domyślnie true. Nie przechowujemy jawnego tokenu.
`limits.group_limit` / `GroupLimit`: id, group_id, period (Week=1, Month=2,
Year=3), dodatnia amount numeric(18,2), unikalność `(group_id, period)`.
`CategoryGroupLimit` zachowuje wcześniejszy model i tabelę.

Nowe relacje mają NoAction. Relacje między modułami definiuje wyłącznie
AppDbContext. Etap zatwierdzenia modelu obejmował DbSety i konfiguracje;
obecnie Grouping ma również repozytoria, serwisy i API. Ich bieżący zakres
opisuje [status](implementation-status.md).

### Niezmienniki serwisów i pozostałe ograniczenia — 2026-11-10

- Administrator, płatnik, uczestnicy udziałów i obie strony rozliczenia muszą
  spełniać reguły członkostwa właściwej grupy. Serwisy Grouping i Expense
  egzekwują je; sam FK użytkownika nie zapewnia członkostwa. Edycja i usuwanie
  uwzględniają historyczne członkostwa zgodnie z kontraktami modułów.
- Suma kwot udziałów musi równać się kwocie wydatku; agregaty członków muszą być
  aktualizowane atomowo ze źródłami przy utworzeniu, edycji i usunięciu.
  Zapewnia to koordynator Grouping; pełne przeliczenie i uzgadnianie pozostają
  do implementacji. Default 0 i CHECK >= 0 nie utrzymują sald. Seed przelicza
  je wyłącznie dla danych syntetycznych.
- Waluta wydatku i rozliczenia jest walutą grupy; powiązana transakcja bankowa
  musi mieć tę samą walutę i właściwego właściciela (płatnik / nadawca).
- Transakcja bankowa może być wykorzystana łącznie najwyżej raz jako źródło
  wydatku albo rozliczenia. Dwa osobne indeksy UNIQUE zapewniają wyłączność
  jedynie wewnątrz każdej tabeli. Od 2026-11-03 Banking ma wspólny rejestr
  `BankTransactionAssignment` z PK na BankTransactionId i rodzajem konsumenta
  Expense=1 / GroupSettlement=2. Zapewnia jedno przypisanie w rejestrze; atomowe
  utrzymywanie go przez Expense jest zaimplementowane. Grouping nadal odrzuca
  nowe bankowe powiązania rozliczeń; pełna integracja tego konsumenta pozostaje
  do implementacji.
  Wewnętrzny serwis Banking obsługuje odczyt, idempotentne przypisanie i zwolnienie
  zgodnego konsumenta; zapisy wymagają transakcji wywołującego.
  Szczegóły schematu i filtrów są w [kontrakcie Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#transaction-assignments--2026-11-03).
- Zużycie zaproszenia wymaga atomowego sprawdzenia aktywności, wygaśnięcia
  i limitu użyć wraz z dodaniem członkostwa. Realizuje to serwis Grouping
  w transakcji koordynatora; sam CHECK nie realizuje przepływu zaproszeń.

## Kontrakt ręcznego Expense

Przegląd 2026-11-15: frontend pozwala edytować udziały procentem, suwakiem
lub pomocniczą kwotą, blokować wybrane udziały i rozdzielać resztę po zmianie
uczestników. `shareDistribution.ts` bilansuje setne procenta do 100%; kwota
w UI jest przeliczana na procent. Blokady i tryb edycji nie są utrwalane w API.
Rozdział groszy przez backend pozostaje odrębnym obliczeniem na zapisanych
procentach; formularz nie zmienia domenowego modelu ExpenseShare.

DTO są w `Expense/DTOs/Expenses`, interfejs w `Services/Expenses/IExpenseService`.
DTO Expense zawierają wyłącznie dane. Normalizacja, walidacja, parsowanie liczb
i sprawdzanie sum są w `Services/Expenses/ExpenseRequestService`.
Serwis zwraca ExpenseView z ExpenseDetails i etykietami; kontroler mapuje je na
ExpenseResponse z tekstowymi liczbami, zgodnie ze wzorcem Banking.
Kontrakt 10 operacji, DTO, preview, listy i źródła opisują
[Expense API](../notes/EXPENSE_API.md) i [OpenAPI](../notes/EXPENSE_API.openapi.yaml)
(weryfikacja 2026-11-07). Działają kategorie, dostępne źródła bankowe, historia,
szczegóły, preview tworzenia i edycji, POST, PUT i DELETE Expense oraz analiza
i odczyt paragonu. ExpenseService integruje odczyty metadanych/nazw oraz wspólną
prywatną ścieżkę przygotowania preview i zapisu. Kompletny zapis i aktualizacja
agregatów pozostają jedną transakcją. Grouping dostarcza rzeczywiste członkostwa
i metadane we wszystkich środowiskach. Szczegóły i uruchomienie opisuje kontrakt API.
Kwoty, ilości i procenty są tekstami
dziesiętnymi. Od 2026-11-07 `transactionTime` w API Expense jest datą `YYYY-MM-DD`
(POST/PUT/preview, szczegóły, historia i filtry). W bazie pozostaje `DateTime` /
`timestamptz`; nowe zapisy mają północ UTC, bez migracji. Starsze daty odczytujemy
z części UTC istniejącego timestampu. Brak Amount oznacza sumę produktów;
podane Amount jest celem z dopełnieniem „Nieznane”. Wynik musi być dodatni.
Rabat produktu nie może przekroczyć jego wartości; udziały są unikalne i sumują
się do 100%. Odpowiedź zawiera wszystkie pozycje i udziały z kwotami.

Przegląd UI 2026-11-17: frontend zawęża kategorię „Zniżka” do ujemnej ceny,
ilości 1 i rabatu 0; wybranie kategorii ustawia ilość i rabat. To ograniczenie
formularza, nie dodatkowa reguła kategorii w API. Ogólny backendowy model nadal
dopuszcza ujemne ceny niezależnie od kategorii i wymaga dla nich rabatu 0.
Rabat jest kwotą całej pozycji: cena × ilość + rabat; nie jest procentem.
Powiązanie bankowe Expense istnieje także wtedy, gdy edytujący nie ma dostępu
do prywatnych szczegółów transakcji. UI identyfikuje jej właściciela przez
płatnika; nie należy nazywać go autorem wydatku. Szczegóły zachowania formularza
i granice weryfikacji opisuje [status](implementation-status.md).

Ochronę przed duplikatami przy ponawianiu zapisu odłożono do kolejnego etapu;
obecny zapis jej nie gwarantuje.
Systemowa kategoria „Nieznane” ma stałe ID i nie jest dostępna dla ręcznych pozycji.
Grouping sprawdza walutę i członkostwa pod blokadą grupy oraz aktualizuje salda.
ExpenseAdded powiadamia pozostałych aktualnych członków grupy z aktywnymi kontami
przy tworzeniu wydatku. Edycja i usuwanie nie powiadamiają.

## Dane zakupowe dla Reports

IExpenseAggregationService udostępnia agregaty kategorii, okresów i dat:
koszt z udziału w zakresie Personal oraz pełne kwoty w zakresie Group.
Koszt kategorii wynika z udziału w całym wydatku, bez przypisywania
produktów do osób; uwzględnia rabaty, pozycje ujemne i systemowe „Nieznane”.
Raporty nie doliczają rozliczeń ani ponownie paragonów/transakcji bankowych.
Daty raportów są obustronnie włączne. [Kontrakt](../notes/EXPENSE_REPORTS.md)
opisuje filtry, uprawnienia i rozdzielanie groszy.

## Import Banking

Refresh identyfikuje rachunki przez `(TinkUserId, TinkAccountId)`, zachowując
lokalne GUID i historię. Zniknięcie z pełnej listy ustawia `IsClosed`, a ponowne
pojawienie otwiera rachunek. Starsze rekordy bez identyfikatorów Tink wymagają
osobnego uzgodnienia; nie są dopasowywane po numerze. Numer, IBAN i identyfikator
instytucji mogą nie być dostępne, zwłaszcza dla danych syntetycznych.

Kwoty BankTransaction zachowują znak Tink (obciążenie ujemne, uznanie dodatnie),
a migracja `AddBanking` usuwa historyczny CHECK `amount > 0` z SQL init.
Dokładność pozostaje `decimal(18,2)`; import nie zaokrągla większej precyzji.
Zdalne `BOOKED` zapisujemy jako lokalne `Booked`. Import nie tworzy Expense.
`LastSuccessfulSyncAt` dotyczy zakończenia synchronizacji rachunku,
`TransactionsSyncedThrough` wyznacza kolejny zakres pobierania. Szczegóły i
ograniczenia opisuje [kontrakt Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md#refresh-on-demand).

## Unikalność źródeł wydatku

`ExpenseConfiguration` i snapshot definiują dwa osobne indeksy unikalne:

- `c_expense_unique_fk_bank_transaction_id` na `bank_transaction_id`;
- `c_expense_unique_fk_receipt_id` na `receipt_id`.

Migracja `20261021162958_AddUniqueExpenseSourceReferences` tworzy je w tabeli
`expense.expense`. Po jej zastosowaniu ta sama transakcja lub paragon mogą być
przypisane do najwyżej jednego wydatku. Obie kolumny pozostają nullable;
wiele wydatków może nie mieć transakcji lub paragonu. Unikalność dotyczy każdego
FK osobno, globalnie między grupami. Indeks złożony z `Id` i FK nie zapewniłby
tej reguły, ponieważ `Id` jest już unikalnym kluczem głównym.

Opis bazy w `doc` uwzględnia te unikalności, historyczny SQL init ich nie zawiera.
Modele nadal przechowują same identyfikatory, bez nawigacji między modułami
i konfiguracji `.WithOne()`. Indeksy wymuszają unikalność w bazie po migracji,
ale nie dodają relacji nawigacyjnych EF. Obecność plików migracji nie potwierdza
ich zastosowania na konkretnej bazie.

## Konto i tokeny użytkownika

`User.Email` jest wymagany (`NOT NULL`) i unikalny wśród aktywnych kont.
Migracja `20261109191600_UserImprovements` zastępuje globalne UNIQUE
indeksem częściowym `c_user_unique_email` z filtrem `is_active = true`.
Wiele nieaktywnych kont może zachować ten sam adres. Login pozostaje unikalny
globalnie. Nowe konto z ponownie użytym adresem otrzymuje nowe ID i nie przejmuje
historii ani danych poprzedniego konta. Data urodzenia jest wymagana.
`User.IsEmailConfirmed` jest mapowane na `user.user.is_email_confirmed`
(`boolean NOT NULL DEFAULT false`) i dotyczy aktualnego adresu. Pole występuje
w opisie bazy, diagramie logicznym, konfiguracji EF i snapshocie. Dodaje je
migracja `20261018151534_AddUserTokenTable`. Lokalna rejestracja ustawia `false`,
a poprawne potwierdzenie tokenem ustawia `true`. Pola `RefreshToken` oraz
`RefreshTokenExpiryTime` zostały usunięte z `User`; tokeny należą do osobnej
tabeli `user.user_token`.

Od `20261109191600_UserImprovements` `PasswordHash` jest opcjonalny,
a `GoogleSubject` to nullable text: trwały identyfikator konta Google z claimu
`sub`, niezależny od adresu e-mail. Indeks `c_user_unique_google_subject` jest
unikalny globalnie, także dla nieaktywnych kont; wiele NULL jest dozwolonych.
CHECK `c_user_authentication_method` wymaga hasła lub GoogleSubject. Nie ma
osobnej tabeli tożsamości. Down blokuje tabelę i odmawia cofnięcia, jeżeli
pozostały konta bez lokalnego hasła; nie generuje im zastępczych haseł.

Rejestracja Google tworzy konto bez hasła. Potwierdzony adres Gmail lub
Google Workspace może otrzymać IsEmailConfirmed=true i sesję od razu;
pozostałe adresy przechodzą potwierdzenie SMTP. GoogleSubject jest wyłącznym
kluczem dopasowania konta przy logowaniu Google. Zgodny e-mail nie łączy kont,
zmiana e-maila w Google nie nadpisuje User.Email, a nieaktywny sub nie może być
ponownie zarejestrowany. Konto Google zachowuje zwykły, wymagany login.
Nie ma odłączania Google ani zmiany adresu przez aplikację, również po ustawieniu
hasła. Pierwsze hasło wykorzystuje istniejący PasswordReset i SMTP, bez nowego
purpose/tabeli; wymaga aktywnego konta z potwierdzonym adresem. Po ustawieniu
hasła obie metody logowania działają. Profil ujawnia tylko hasPassword oraz
isGoogleLinked, bez hasha i GoogleSubject. Kontrakt i ograniczenia wdrożenia
opisuje [development](development.md#rejestracja-i-logowanie-google).

Relacja `User` → `UserToken` jest 1:N. `FkUserId` jest wymagany, a usunięcie
użytkownika kaskadowo usuwa jego tokeny. Pola tokenu:

| Pole | Znaczenie |
| --- | --- |
| `Id` | Identyfikator wpisu, mapowany na `_id` |
| `FkUserId` | Właściciel tokenu |
| `TokenHash` | Unikalny skrót tokenu, bez przechowywania jawnego tokenu |
| `Purpose` | Enum zapisany jako integer: 1 — EmailConfirmation, 2 — EmailChange, 3 — PasswordReset, 4 — RefreshToken, 5 — AccountDeactivation |
| `TargetEmail` | Wymagany dla purpose 1–3 i 5, NULL dla purpose 4 |
| `CreatedAt` | Utworzenie tokenu; domyślnie CURRENT_TIMESTAMP |
| `ExpiresAt` | Wygaśnięcie tokenu, późniejsze niż CreatedAt |

UNIQUE `(FkUserId, Purpose)` pozwala na jeden aktualny token danego rodzaju
na konto, w tym jedną sesję odnawialną. Konfiguracja EF zawiera również CHECK
rodzaju tokenu, terminu ważności i zależności `TargetEmail` od `Purpose`.
`TargetEmail` pozostaje nullable w C# i SQL, ponieważ refresh token wymaga NULL.
To CHECK wymusza wartość różną od NULL (IS NOT NULL) dla purpose 1–3 i 5;
nie ustawiaj NOT NULL na całej kolumnie. Dla purpose 5 zapisujemy aktualny
adres użytkownika, na który wysyłamy token dezaktywacji.
Przy zmianie e-maila nowy adres czeka w `TargetEmail`; zastępuje `User.Email`
dopiero po potwierdzeniu. Nie dodajemy `PendingEmail` do użytkownika.

Uzgodnione działanie serwisów: ponowne żądanie zastępuje
token danego rodzaju, użyty token jednorazowy jest usuwany razem ze zmianą
danych, a refresh token jest wymieniany przy odświeżeniu i usuwany przy
wylogowaniu. Access tokenów JWT nie zapisujemy w bazie. Sam model i repozytorium
nie zapewniają tych zachowań. Serwis potwierdzania e-maila używa warunkowego
usunięcia niewygasłego tokenu i warunkowego potwierdzenia aktualnego adresu
aktywnego konta w jednej transakcji. Rejestracja zapisuje konto i token razem,
wysyła wiadomość i dopiero po przyjęciu przez SMTP zatwierdza transakcję.
Błąd wysyłki wycofuje konto i token. Pozostałe żądania mailowe także zachowują
poprzedni token przy błędzie SMTP. Ponowna wysyłka zastępuje token po wymaganym
odstępie. Ważność to 24 godziny, odstęp to 60 sekund — wartości domyślne
z `EmailConfirmationOptions`. Host rejestruje te opcje bez wiązania
z konfiguracją, więc JSON i env obecnie ich nie nadpisują.
Potwierdzanie i ponowna wysyłka blokują najpierw rekord
użytkownika, a następnie operują na tokenie. Reset zapomnianego hasła jest
zaimplementowany: anonimowe żądanie dla aktywnego konta z potwierdzonym e-mailem
zapisuje hash tokenu PasswordReset z aktualnym adresem w TargetEmail i wysyła
token przez SMTP. Odpowiedź 202 jest jednakowa również dla nieznanego adresu,
niedostępnego konta, cooldownu i błędu SMTP. Token jest domyślnie ważny 30 minut;
odstęp między wysyłkami wynosi 60 sekund, a późniejsze żądanie zastępuje token.
Reset sprawdza zgodność TargetEmail z aktualnym adresem, jednorazowo zużywa
token i w jednej transakcji zmienia hash hasła oraz usuwa wszystkie pozostałe
tokeny konta, w tym sesję i oczekującą zmianę e-maila. Wymaga ponownego logowania.
Zmiana e-maila wymaga aktywnej sesji i aktualnego hasła; nowy
adres czeka w tokenie EmailChange do potwierdzenia przez SMTP. Potwierdzenie
zużywa token, ponownie sprawdza unikalność adresu i zmienia User.Email,
zachowując IsEmailConfirmed = true. W tej samej transakcji usuwa wszystkie
pozostałe tokeny konta, również sesję; użytkownik musi zalogować się ponownie.
Ponowne żądanie po odstępie zastępuje oczekujący token. Domyślnie token jest
ważny 24 godziny, odstęp wynosi 60 sekund; opcje EmailChange są wiązane
z konfiguracją hosta. Login, refresh
i logout są zaimplementowane. JWT nie jest zapisywany w bazie; zawiera ID konta
i ID sesji, której aktualność jest sprawdzana przy uwierzytelnieniu. Refresh
rotuje hash tokenu, zachowując ID i bezwzględny termin sesji (domyślnie 7 dni).
Nowe logowanie nadaje nowe ID sesji; wylogowanie usuwa sesję. Oba zdarzenia
unieważniają także wcześniejsze JWT. Konfigurację i kontrakty opisuje development.md.

## Zdjęcie profilu

Migracja `20261109191600_UserImprovements` dodaje opcjonalny text
User.IconStorageKey i CHECK `c_user_icon_source`:
`icon IS NULL OR icon_storage_key IS NULL`. Icon oznacza klucz presetu,
IconStorageKey klucz własnego zdjęcia w S3; oba NULL oznaczają brak zdjęcia.
Nie zapisujemy podpisanych URL-i w bazie. Nie migrujemy dawnych dowolnych
wartości Icon do presetów ani nie uploadujemy plików. Odczyt dawnego Icon,
które nie jest dostępnym presetem, zwraca 404. Down usuwa nową kolumnę i CHECK,
nie usuwa obiektów S3; traci powiązania do własnych zdjęć użytkowników.

Edycja bez instrukcji ikony zachowuje zdjęcie; removeIcon jawnie je usuwa.
Presety są współdzielone i nie podlegają usuwaniu przy zmianie wyboru.
Zasady uploadu, kompensacji i konfiguracji prefiksów opisuje
[development](development.md#zdjęcia-profilu-i-prefiksy-storage).

## Dezaktywacja konta

Dezaktywacja wymaga aktywnej sesji do wysłania tokenu na aktualny e-mail.
Token AccountDeactivation jest jednorazowy, domyślnie ważny 30 minut,
z odstępem 60 sekund między wysyłkami. Potwierdzenie jest anonimowe i wymaga
zgodności TargetEmail z aktualnym adresem aktywnego, potwierdzonego konta.
W jednej transakcji zużywa token, ustawia IsActive = false i usuwa wszystkie
pozostałe tokeny konta. Rekord User i dane finansowe pozostają w bazie;
login nadal jest zajęty, a e-mail można ponownie wykorzystać przy rejestracji
lub zmianie adresu aktywnego konta. Brak reaktywacji; przyszła reaktywacja musi
uwzględniać zajętość adresu. JWT nie jest usuwany z klienta,
ale kolejne uwierzytelnienie odrzuca go po sprawdzeniu sesji i IsActive w bazie.
Purpose 5 i rozszerzone ograniczenia CHECK dodaje migracja
`20261023131244_AddAccountDeactivationTokens`. Aktualny opis `Baza danych.txt`,
enum, konfiguracja EF i snapshot są zgodne: purpose 1–5, TargetEmail wymagany
dla 1–3 i 5 oraz NULL dla 4. Ta zgodność nie potwierdza zastosowania migracji
na konkretnej bazie. Down usuwa oczekujące tokeny rodzaju 5 przed przywróceniem
CHECK dla 1–4; nie reaktywuje kont.

## Daty i czas

Opis bazy i aktualne mapowania EF używają `timestamp with time zone` dla
`User.CreatedAt`, `User.LastLoginTime`, `UserToken.CreatedAt`,
`UserToken.ExpiresAt`, `Expense.TransactionTime`, `Notification.CreatedAt`
i `NotificationRecipient.ReadAt`. Zmiana `Notification.SentAt` na `CreatedAt`
jest uwzględniona w migracji `20261027172612_AddNotificationsAfterRebase`.
W C# pozostaje `DateTime` / `DateTime?`; zapis powinien używać UTC.
`User.BirthDate` i `BankTransaction.Date` pozostają `DateOnly` / SQL `date`.
Przy konwersji istniejących kolumn bez strefy sprawdź znaczenie dotychczasowych
wartości i strefę sesji bazy. SQL inicjalizacyjny opisuje wcześniejszy stan;
typy aktualizują migracje.

## Zachowania wynikające z wymagań

- Źródła danych mają zasilać spójny obraz wydatków; transakcja bankowa i paragon
  mogą opisywać ten sam zakup.
- OCR i kategoryzacja dają wynik możliwy do ręcznej korekty.
- Powiązanie paragonu z transakcją jest propozycją do akceptacji lub odrzucenia
  według historyjki 1.6; automatyczne dopasowanie jest rozszerzeniem z sekcji 3,
  a nie wymaganiem MVP.
- Podział kosztów prowadzi do prezentacji zobowiązań; przekazanie środków
  następuje poza aplikacją. MVP obejmuje uczestników i udziały na poziomie całego
  wydatku, nie rozliczanie uczestników poszczególnych produktów.
- Analiza osobista i grupowa oraz kategorie produktów są istotnymi częściami
  produktu, nie należy sprowadzać ich wyłącznie do klasyfikacji całej płatności.

Model `Notification` ma typ (General = 0, ExpenseAdded = 1, LimitApproaching = 2,
LimitExceeded = 3, GroupMemberJoined = 4, SettlementReceived = 5), tytuł, treść, CreatedAt oraz opcjonalne FkGroupId i FkExpenseId.
FkExpenseId zastępuje wcześniejszą parę TargetType/TargetId; nawigacja prowadzi
do wydatku, jeśli jest wskazany, w przeciwnym razie do wskazanej grupy.
FK do Expense jest skonfigurowany we wspólnym AppDbContext, aby uniknąć cyklu
zależności projektów; usunięcie wydatku ustawia fk_expense_id na NULL,
zachowując historię powiadomień. `NotificationRecipient`
ma klucz (FkNotificationId, FkUserId) i opcjonalny ReadAt; NULL oznacza nieodczytane.
Odczyt jest indywidualny dla każdego odbiorcy.
`NotifyAsync` od 2026-10-28 tworzy przypisania tylko dla istniejących aktywnych
kont zwróconych przez lookup User. Nieaktywne konto może zachować członkostwo
w grupie, ale jest pomijane przy tworzeniu nowych powiadomień. Brak aktywnych
odbiorców nie blokuje operacji biznesowej: metoda zwraca null bez zapisu.
Jest to reguła tworzenia nowych przypisań; nie usuwa historycznych powiadomień.

Migracja
`20261027172612_AddNotificationsAfterRebase` zastępuje usuniętą przy rebase
`AddNotificationRecipient`. Zmienia nazwę sent_at na created_at bez utraty dat,
uzupełnia title pustym tekstem i type wartością 0 oraz tworzy odbiorców bez
przypisywania ich do starych powiadomień. Seed wymaga zastosowania aktualnego
łańcucha migracji. Zastosowanie migracji na konkretnej bazie sprawdzaj oddzielnie.

Docelowo FkExpenseId wskazuje wydatek, a FkGroupId służy powiadomieniom o samej
grupie, dołączeniu i limitach; grupę wydatku można odczytać przez Expense.
NotifyAsync odrzuca jednoczesny zapis obu FK; aktualny seed również stosuje
pojedynczy cel. Konfiguracja EF nie ma CHECK ich wzajemnej wyłączności,
więc jest to reguła serwisu, a nie ograniczenie bazy. Seed zawiera przykłady
ExpenseAdded, General i GroupMemberJoined oraz odczyt per odbiorca.

Diagram logiczny przedstawia po stronie grupy Notification → Group liczność 1,
natomiast kod dopuszcza FkGroupId = NULL; opis TXT nie oznacza tego pola jako
NOT NULL. Nie wyprowadzaj obowiązkowego powiązania z grupą z samego diagramu.
NotificationRecipient realizuje relację N:M użytkowników i powiadomień,
z indywidualnym ReadAt. Obie encje należą do Notifications na diagramie podziału.

`Receipt.StorageKey` jest mapowane na wymagane `receipt.receipt.storage_key`
z unikalnym indeksem `ux_receipt_storage_key`. Model, konfigurację, snapshot
i migrację `20261028184348_RenameReceiptS3LinkToStorageKeyAndMakeUnique`
sprawdzono 2026-10-28. Migracja zmienia nazwę `s3_link` bez przekształcania
istniejących wartości i dodaje unikalność; nie zamienia dawnych URL-i w klucze
obiektów. StorageKey przechowuje klucz pliku, nie pełny adres URL.
`ExtractionResult` zawiera surową odpowiedź OCR: obiekt `receipt`, tablicę
`warnings` i boolean `needs_review`. Nie tworzy automatycznie pozycji wydatku
ani powiązań kategorii. Seed stosuje ten format i syntetyczny klucz storage,
ale nie tworzy odpowiadającego mu pliku w magazynie.

Kontrakt workera sprawdzono statycznie 2026-10-29. Kwoty i ilości w JSON są
tekstami dziesiętnymi, data ma format ISO, a nieznane wartości są `null`.
Pozycja zachowuje odczytaną `name` i osobną propozycję `name_corrected`, kategorię
oraz `needs_review` / `review_reason`. Nazwy kategorii pochodzą z taksonomii v2
w [workerze](../workers/extraction_worker/extraction/categories.py); od migracji
`CompleteExpenseModel` 19 kategorii produktowych oraz systemowe „Nieznane”
są zmapowane 1:1 w tabeli `expense.category` z deterministycznymi ID.
Rabaty produktów opisuje `discount`; samodzielna zniżka pozostaje pozycją
z kategorią `zniżka` i ujemną kwotą. Wydane kaucje i ich zwroty są osobno
w `deposits` i `deposit_returns`. `amount_due` jest osobnym polem od `total`.
Główna flaga `needs_review` wynika z ostrzeżeń lub flag przeglądu pozycji;
odpowiedź HTTP 200 może nadal wymagać ręcznej korekty.

## Kwestie wymagające ustalenia przy zadaniu

Wydatki osobiste korzystają ze zwykłych grup, w tym grupy startowej opisanej
powyżej; nie ma odrębnego typu ani blokad grupy osobistej.
Dokumentacja nie rozstrzyga zaokrąglania udziałów, obsługi wielu walut,
szczegółowych progów i okresów alertów ani zasad deduplikacji importu.
Przy pracach nad MVP nie rozszerzaj podziału kosztów na produkty tylko dlatego,
że ExpenseItem ma opcjonalny FkUserId. To osobna funkcjonalność rozszerzająca.
Zatwierdzony model płatnika i ewidencji rozliczeń opisano powyżej; nadal brakuje
serwisów realizujących te przepływy.
Nie dodawaj tych reguł wyłącznie na podstawie nazw kolumn.

Opis bazy zawiera zapis `ExpenceShare`; aktualna klasa to `ExpenseShare`.
Różnicę schematu tej tabeli między SQL inicjalizacyjnym a modelem wyjaśnia
[status implementacji](implementation-status.md).

Przegląd opisu bazy, diagramu logicznego i mapowań EF z 2026-10-21:

- Diagram, opis bazy i mapowanie EF używają `birth_date` (`User.BirthDate`).
- Diagram przedstawia powiązania Expense z Receipt i BankTransaction jako
  maksymalnie 1:1. Opis bazy, konfiguracja EF i migracja unikalności są zgodne
  w tym zakresie; sam SQL init nie wymusza tych kardynalności.
- Część FK między modułami istnieje w SQL inicjalizacyjnym, ale nie ma ich
  w konfiguracjach EF i snapshocie. Przegląd kodu 2026-11-15 potwierdza,
  że Expense → Group/Receipt/BankTransaction i ExpenseShare → User nadal
  nie mają jawnego mapowania EF; wcześniejszy opis ich obecności w AppDbContext
  był niezgodny z kodem. AppDbContext mapuje płatnika i Notification → Expense.
  Samo pole `Fk...Id` nie definiuje relacji EF. Przy zmianach schematu porównuj
  model z bazowym SQL i migracjami.
- `BankAccount.IsClosed` mają DEFAULT false w opisie
  i SQL inicjalizacyjnym, ale konfiguracje EF nie ustawiają tego domyślnego
  ograniczenia. W C# domyślna wartość `bool` również wynosi false.
- `Receipt.ExtractionResult` jest mapowane jako PostgreSQL `jsonb`; zapis
  `json/text` w opisie bazy jest mniej szczegółowy.

## Źródła

- [Słownik pojęć](../../doc/Słownik%20pojęć.txt)
- [Opis tabel i atrybutów](../../doc/Baza%20danych.txt)
- [Model konceptualny](../../doc/Model%20konceptualny.png) i [logiczny](../../doc/Model%20logiczny.png)
- [Podział encji na moduły](../../doc/Podział%20encji%20na%20moduły.png)
- [Aktualne moduły, modele i konfiguracje](../backend/ZPI4M.Backend/src/modules/)
- [Migracja unikalności źródeł wydatku](../backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/20261021162958_AddUniqueExpenseSourceReferences.cs)
- [Wymagania funkcjonalne](../../doc/Funkcjonalności.txt)

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

## Zaproszenia grupowe — 2026-11-01
GroupInvitationService implementuje generowanie, listę, odwołanie, podgląd
i akceptację. Token to 32 losowe bajty Base64URL; w bazie pozostaje SHA-256.
Ważność jest dowolnym przyszłym terminem. IsActive oznacza odwołanie,
nie dostępność wynikającą z czasu/licznika. Były członek jest reaktywowany
bez zmiany daty i agregatów. Obecny członek nie zużywa ponownie zaproszenia.
Mutacje blokują grupę, potem zaproszenie. Dołączenie zapisuje licznik,
członkostwo i GroupMemberJoined w transakcji wspólnej z Notifications.
Brak push i migracji. Szczegóły: README Grouping.

## Członkostwo grup — 2026-11-01
GroupMemberService obsługuje listę, szczegóły, opuszczenie i usunięcie członka.
Listy obsługują active/former/all (domyślnie active), paginację i kierunek
sortowania po UserId. Odczyty dostępne aktywnym członkom niearchiwalnej grupy.
Szczegóły mogą dotyczyć także byłego członka, ale były członek nie odczytuje grupy.
Leave/Remove zmieniają tylko IsMember=false; historia, JoinedAt i wszystkie
agregaty pozostają. Niezerowe saldo nie blokuje operacji. Powtórzenie daje 204,
brak członkostwa 404; administrator musi wcześniej przekazać rolę (409).
Usunięcie nie jest banem: ważne zaproszenie pozwala wrócić. Brak nowych powiadomień.
Zapisy korzystają z GroupingTransactionCoordinator, blokując najpierw grupę.
Nie ma migracji ani obliczania/utrzymywania sald w tym serwisie.

## Rozliczenia ręczne i agregaty — 2026-11-02
GroupSettlementService implementuje CRUD rozliczeń wykonanych poza aplikacją.
Aktualizuje wkład przez osobny GroupMemberAggregateService we wspólnej transakcji.
Obie strony mogą być historycznymi członkami; wykonujący musi być aktualnym
członkiem i nadawcą albo administratorem. Przy edycji zwykły użytkownik pozostaje
nadawcą przed i po zmianie. Data nie może być przyszła, kwota nie jest ograniczana
saldem. BankTransactionId niepusty daje 400; Banking i wyłączność źródeł pozostają
na osobną turę. Powiązane historyczne rekordy można odczytać lub usunąć; PUT
z null usuwa istniejące powiązanie.

IGroupMemberAggregateService udostępnia ApplySettlementChangeAsync i
ApplyExpenseChangeAsync(previous,current). Null po jednej stronie oznacza
utworzenie/usunięcie; obie null są błędem. Kontrakty wartości należą do Grouping,
bez encji Expense. Płatnik/udziały aktualizują TotalPaid/TotalShare, nadawca/odbiorca
TotalSettlementsSent/Received. Różnice są scalane per użytkownik i sprawdzane
przed mutacją; wynik nie może być ujemny ani przekraczać numeric(18,2).
Historyczne członkostwa są zachowane, brakujące nie są tworzone.
Serwis wymaga aktywnego koordynatora (EnsureActive), blokuje grupę, nie robi
SaveChanges ani commitu. Wywołanie musi nastąpić dokładnie raz ze zmianą źródła.
Tworzenie, edycja i usuwanie Expense dołączają ExpenseDbContext do koordynatora
i przekazują kwotowe udziały pod tą samą blokadą grupy.
Odbudowa/uzgadnianie pozostają do wykonania; tworzenie Expense zapisuje również ExpenseAdded.
Integracja nie wymaga migracji.

## Prywatność powiązania bankowego rozliczenia — 2026-11-02
Grouping zależy od publicznego IBankingReadService, bez zależności odwrotnej.
Lista, szczegóły i odpowiedzi zapisów ujawniają BankTransactionId tylko po
poprawnym odczycie dla ID zalogowanego użytkownika (nie nadawcy rozliczenia).
Administrator grupy nie uzyskuje uprawnień do cudzej bankowości. Brak odczytu
lub kontrolowany błąd Banking daje null bez ukrywania samego rozliczenia.
Nieoczekiwane wyjątki propagują się bez fallbacku ujawniającego ID.
Sprawdzenia listy są kolejne, z lokalnym cache dla jednej odpowiedzi.
Brak nowego interfejsu przypisań, tabeli i migracji. POST/PUT nadal odrzucają
niepuste BankTransactionId. Edycja/null i DELETE historycznych powiązań
pozostają dozwolone; wymagają zastąpienia claim/release przy wdrożeniu Banking.
Walidacja wskazuje konkretne pola zamiast wspólnego komunikatu bankowego.

## Podział migracji Grouping i Expense — stan po rebase 2026-11-10

Po `20261102215301_AddGroupingModel` następują
`20261103165622_CompleteExpenseModel`,
`20261103203528_AddBankTransactionAssignments` oraz
`20261109191600_UserImprovements`. Usunięta tymczasowa migracja
`AddTemporaryExpenseAggregationSupport` nie należy do bieżącego łańcucha.
ExpenseShare przechowuje Percentage, a ExpenseItem nie ma FkUserId.
Szczegóły modelu są w sekcji [relacji](#relacje-i-reprezentacja), a osłony
Up/Down i postępowanie ze starszą bazą w
[instrukcji migracji](development.md#migracje-po-rebase-expensegrouping--2026-11-03).
Nowa baza wymaga SQL init → wszystkie migracje → seed. Usunięcie pliku migracji
nie cofa schematu ani historii istniejącej bazy.

## Sugestie rozliczeń — 2026-11-02
GET /api/groups/{groupId}/settlements/suggestions: aktywny członek
niearchiwalnej grupy. Spójny odczyt grupy, autoryzacji i wszystkich agregatów
w jednym SQL AsSingleQuery/AsNoTracking, bez blokad zapisów i transakcji
koordynatora. Uwzględnia także byłych członków. Saldo = Paid - Share + Sent -
Received. Suma musi wynosić dokładnie zero, a końcowe liczniki Sent/Received
muszą mieścić się w numeric(18,2); w przeciwnym razie 409.
Osobny kalkulator stosuje deterministyczne największy dłużnik/największy
wierzyciel (kolejki priorytetowe, remisy po UserId), O(n log n), decimal.
Nie gwarantuje minimum; DTO jawnie zwraca optimalityGuaranteed=false.
Sugestie są odczytem i nie stanowią zlecenia płatności ani zapisów rozliczeń.
Bez migracji, Banking, odczytów Expense i odbudowy agregatów.
Aktualne kontrakty HTTP: [README Grouping](../backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/README.md).
Przegląd 2026-11-15: lista grup obsługuje `sortBy=name|joinedAt`; `joinedAt`
dotyczy członkostwa bieżącego użytkownika. Odpowiedzi członków mają opcjonalne
`displayName` z lookupu User, również dla byłych członków. Jest to etykieta
odpowiedzi, bez nowego pola encji ani migracji.
