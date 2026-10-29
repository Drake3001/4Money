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
| `GroupMember` | Członkostwo użytkownika w grupie, z flagą administratora; Grouping |
| Wydatek / `Expense` | Zakup pochodzący z ręcznego wpisu, paragonu lub banku; Expense |
| Produkt / `ExpenseItem` | Pozycja zakupu: produkt, usługa lub towar, z kategorią; Expense |
| `Category` | Kategoria produktów, np. żywność, ubrania lub subskrypcje; Expense |
| `ExpenseShare` | Procentowy udział użytkownika w wydatku; Expense |
| Paragon / `Receipt` | Zdjęcie lub zrzut potwierdzenia zakupu; odnośnik do obrazu i surowy wynik OCR; Receipt |
| `BankAccount` | Rachunek bankowy przypisany użytkownikowi; Banking |
| `BankConnectionAttempt` | Trwała próba połączenia z bankiem, skrót state, termin i wynik; Banking |
| `BankConnection` | Lokalne połączenie z bankiem; stan połączenia odrębny od synchronizacji; Banking |
| `BankTransaction` | Transakcja zaimportowana z rachunku, odrębna od wydatku; Banking |
| Limit / `CategoryGroupLimit` | Próg dla kategorii w grupie; Limits. Model `Group` ma też ogólne limity. |
| `Notification` / `NotificationRecipient` | Treść powiadomienia z opcjonalnym FK grupy albo wydatku oraz odbiorcy z indywidualną datą odczytu; Notifications; migracja AddNotificationsAfterRebase |
| Raport | Agregacja danych, a nie osobna encja w obecnym projekcie bazy; Reports |

Dokumentacja opisuje zakup jako jeden lub wiele produktów; diagram i aktualny
model dopuszczają kolekcję bez pozycji. Wymagane zachowanie przy tworzeniu
wydatku należy ustalić przy implementacji przepływu.

## Relacje i reprezentacja

- `Expense` należy do grupy; `FkGroupId` jest wymaganym `Guid`. Powiązania
  z paragonem i transakcją bankową są opcjonalne i maksymalnie 1:1.
- `ExpenseItem` należy do wydatku i kategorii; przypisanie do użytkownika
  jest opcjonalne. Zawiera cenę jednostkową i ilość. Pole FkUserId istnieje
  w modelu i opisie bazy, ale przypisywanie uczestników do produktów jest
  rozszerzeniem poza MVP według sekcji 3 `Funkcjonalności.txt`.
- `ExpenseShare` łączy wydatek i użytkownika kluczem złożonym oraz procentem.
  Nie jest zapisem wykonanego przelewu.
- `GroupMember` łączy użytkownika i grupę kluczem złożonym; użytkownik może
  należeć do wielu grup.
- `BankAccount` należy do użytkownika, a `BankTransaction` do rachunku.
  Identyfikatory dostawcy występują w modelu bankowym, ale nie są automatycznie
  publicznym kontraktem API.
  Aktualny opis bazy wskazuje opcjonalne credentials_id oraz UNIQUE
  (fk_user_id, financial_institution_id, number); obecna konfiguracja Banking
  uwzględnia oba elementy. Nie zakładaj ich wdrożenia bez odpowiedniej migracji.
- `CategoryGroupLimit` łączy kategorię i grupę. Modele przewidują opcjonalne
  limity tygodniowe, miesięczne i roczne.

Aktualne konfiguracje EF używają m.in. `decimal(18,2)` dla kwot,
`decimal(18,3)` dla ilości i `decimal(5,2)` dla udziału procentowego.
Zachowuj reprezentację dziesiętną pieniędzy; nie zmieniaj precyzji bez analizy
modelu i migracji.

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

- `c_expense_unique_fk_bank_transaction_id` na `fk_bank_transaction_id`;
- `c_expense_unique_fk_receipt_id` na `fk_receipt_id`.

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

`User.Email` jest wymagany (`NOT NULL`) i unikalny. Data urodzenia jest wymagana.
`User.IsEmailConfirmed` jest mapowane na `user.user.is_email_confirmed`
(`boolean NOT NULL DEFAULT false`) i dotyczy aktualnego adresu. Pole występuje
w opisie bazy, diagramie logicznym, konfiguracji EF i snapshocie. Dodaje je
migracja `20261018151534_AddUserTokenTable`. Rejestracja ustawia `false`,
a poprawne potwierdzenie tokenem ustawia `true`. Pola `RefreshToken` oraz
`RefreshTokenExpiryTime` zostały usunięte z `User`; tokeny należą do osobnej
tabeli `user.user_token`.

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
a następnie wysyła wiadomość. Ponowna wysyłka zastępuje token po wymaganym
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

## Dezaktywacja konta

Dezaktywacja wymaga aktywnej sesji do wysłania tokenu na aktualny e-mail.
Token AccountDeactivation jest jednorazowy, domyślnie ważny 30 minut,
z odstępem 60 sekund między wysyłkami. Potwierdzenie jest anonimowe i wymaga
zgodności TargetEmail z aktualnym adresem aktywnego, potwierdzonego konta.
W jednej transakcji zużywa token, ustawia IsActive = false i usuwa wszystkie
pozostałe tokeny konta. Rekord User i dane finansowe pozostają w bazie;
login i e-mail nadal są zajęte, brak reaktywacji. JWT nie jest usuwany z klienta,
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
LimitExceeded = 3, GroupMemberJoined = 4), tytuł, treść, CreatedAt oraz opcjonalne FkGroupId i FkExpenseId.
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

## Kwestie wymagające ustalenia przy zadaniu

Dokumentacja nie rozstrzyga sposobu reprezentowania wydatków osobistych przy
wymaganej grupie, algorytmu sald, zaokrąglania udziałów, obsługi wielu walut,
szczegółowych progów i okresów alertów ani zasad deduplikacji importu.
Przy pracach nad MVP nie rozszerzaj podziału kosztów na produkty tylko dlatego,
że ExpenseItem ma opcjonalny FkUserId. To osobna funkcjonalność rozszerzająca.
Brakuje również pełnego kontraktu płatnika i ewidencji spłaty zobowiązań.
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
  w konfiguracjach EF i snapshocie (np. Expense → Group/Receipt/BankTransaction).
  Samo pole `Fk...Id` nie definiuje relacji EF. Przy zmianach schematu porównuj
  model z bazowym SQL i migracjami.
- `BankAccount.IsClosed` i `GroupMember.IsAdmin` mają DEFAULT false w opisie
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
