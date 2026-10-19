# Domena i model danych

Czytaj przed zmianą modeli, wydatków, podziału kosztów, limitów oraz przetwarzania
paragonów. Zakres zachowań opisuje [produkt](product.md), a organizację kodu
[architektura](architecture.md).

## Pojęcia i encje

| Pojęcie / encja | Znaczenie i właściciel |
| --- | --- |
| Konto / `User` | Profil użytkownika aplikacji; moduł User. To nie rachunek bankowy. |
| `UserToken` | Token potwierdzenia lub zmiany e-maila, resetu hasła albo odświeżania sesji; User |
| Grupa wydatkowa / `Group` | Przestrzeń współdzielenia kosztów; Grouping |
| `GroupMember` | Członkostwo użytkownika w grupie, z flagą administratora; Grouping |
| Wydatek / `Expense` | Zakup pochodzący z ręcznego wpisu, paragonu lub banku; Expense |
| Produkt / `ExpenseItem` | Pozycja zakupu: produkt, usługa lub towar, z kategorią; Expense |
| `Category` | Kategoria produktów, np. żywność, ubrania lub subskrypcje; Expense |
| `ExpenseShare` | Procentowy udział użytkownika w wydatku; Expense |
| Paragon / `Receipt` | Zdjęcie lub zrzut potwierdzenia zakupu; odnośnik do obrazu i surowy wynik OCR; Receipt |
| `BankAccount` | Rachunek bankowy przypisany użytkownikowi; Banking |
| `BankTransaction` | Transakcja zaimportowana z rachunku, odrębna od wydatku; Banking |
| Limit / `CategoryGroupLimit` | Próg dla kategorii w grupie; Limits. Model `Group` ma też ogólne limity. |
| `Notification` | Powiadomienie powiązane z grupą w projekcie bazy; Notifications |
| Raport | Agregacja danych, a nie osobna encja w obecnym projekcie bazy; Reports |

Dokumentacja opisuje zakup jako jeden lub wiele produktów; diagram i aktualny
model dopuszczają kolekcję bez pozycji. Wymagane zachowanie przy tworzeniu
wydatku należy ustalić przy implementacji przepływu.

## Relacje i reprezentacja

- `Expense` należy do grupy; `FkGroupId` jest wymaganym `Guid`. Powiązania
  z paragonem i transakcją bankową są opcjonalne.
- `ExpenseItem` należy do wydatku i kategorii; przypisanie do użytkownika
  jest opcjonalne. Zawiera cenę jednostkową i ilość.
- `ExpenseShare` łączy wydatek i użytkownika kluczem złożonym oraz procentem.
  Nie jest zapisem wykonanego przelewu.
- `GroupMember` łączy użytkownika i grupę kluczem złożonym; użytkownik może
  należeć do wielu grup.
- `BankAccount` należy do użytkownika, a `BankTransaction` do rachunku.
  Identyfikatory dostawcy występują w modelu bankowym, ale nie są automatycznie
  publicznym kontraktem API.
- `CategoryGroupLimit` łączy kategorię i grupę. Modele przewidują opcjonalne
  limity tygodniowe, miesięczne i roczne.

Aktualne konfiguracje EF używają m.in. `decimal(18,2)` dla kwot,
`decimal(18,3)` dla ilości i `decimal(5,2)` dla udziału procentowego.
Zachowuj reprezentację dziesiętną pieniędzy; nie zmieniaj precyzji bez analizy
modelu i migracji.

## Konto i tokeny użytkownika

`User.Email` jest opcjonalny i unikalny. Nowe `IsEmailConfirmed` ma domyślnie
wartość `false` i dotyczy aktualnego adresu. Pola `RefreshToken` oraz
`RefreshTokenExpiryTime` zostały usunięte z `User`; tokeny należą do osobnej
tabeli `user.user_token`.

Relacja `User` → `UserToken` jest 1:N. `FkUserId` jest wymagany, a usunięcie
użytkownika kaskadowo usuwa jego tokeny. Pola tokenu:

| Pole | Znaczenie |
| --- | --- |
| `Id` | Identyfikator wpisu, mapowany na `_id` |
| `FkUserId` | Właściciel tokenu |
| `TokenHash` | Unikalny skrót tokenu, bez przechowywania jawnego tokenu |
| `Purpose` | Enum zapisany jako integer: 1 — EmailConfirmation, 2 — EmailChange, 3 — PasswordReset, 4 — RefreshToken |
| `TargetEmail` | Wymagany dla purpose 1–3, NULL dla purpose 4 |
| `CreatedAt` | Utworzenie tokenu; domyślnie CURRENT_TIMESTAMP |
| `ExpiresAt` | Wygaśnięcie tokenu, późniejsze niż CreatedAt |

UNIQUE `(FkUserId, Purpose)` pozwala na jeden aktualny token danego rodzaju
na konto, w tym jedną sesję odnawialną. Konfiguracja EF zawiera również CHECK
rodzaju tokenu, terminu ważności i zależności `TargetEmail` od `Purpose`.
Przy zmianie e-maila nowy adres czeka w `TargetEmail`; zastępuje `User.Email`
dopiero po potwierdzeniu. Nie dodajemy `PendingEmail` do użytkownika.

Uzgodnione działanie serwisów do implementacji: ponowne żądanie zastępuje
token danego rodzaju, użyty token jednorazowy jest usuwany razem ze zmianą
danych, a refresh token jest wymieniany przy odświeżeniu i usuwany przy
wylogowaniu. Access tokenów JWT nie zapisujemy w bazie. Sam model i repozytorium
nie zapewniają jeszcze tych zachowań ani bezpiecznej obsługi współbieżności.

## Daty i czas

Opis bazy i aktualne mapowania EF używają `timestamp with time zone` dla
`User.CreatedAt`, `User.LastLoginTime`, `UserToken.CreatedAt`,
`UserToken.ExpiresAt`, `Expense.TransactionTime` i `Notification.SentAt`.
W C# pozostaje `DateTime` / `DateTime?`; zapis powinien używać UTC.
`User.BirthDate` i `BankTransaction.Date` pozostają `DateOnly` / SQL `date`.
Przy konwersji istniejących kolumn bez strefy sprawdź znaczenie dotychczasowych
wartości i strefę sesji bazy. SQL inicjalizacyjny opisuje wcześniejszy stan;
typy aktualizują migracje.

## Zachowania wynikające z wymagań

- Źródła danych mają zasilać spójny obraz wydatków; transakcja bankowa i paragon
  mogą opisywać ten sam zakup.
- OCR i kategoryzacja dają wynik możliwy do ręcznej korekty.
- Powiązanie paragonu z transakcją jest propozycją do akceptacji lub odrzucenia.
- Podział kosztów prowadzi do prezentacji zobowiązań; przekazanie środków
  następuje poza aplikacją.
- Analiza osobista i grupowa oraz kategorie produktów są istotnymi częściami
  produktu, nie należy sprowadzać ich wyłącznie do klasyfikacji całej płatności.

## Kwestie wymagające ustalenia przy zadaniu

Dokumentacja nie rozstrzyga sposobu reprezentowania wydatków osobistych przy
wymaganej grupie, algorytmu sald, zaokrąglania udziałów, obsługi wielu walut,
szczegółowych progów i okresów alertów ani zasad deduplikacji importu.
Brakuje również pełnego kontraktu płatnika i ewidencji spłaty zobowiązań.
Nie dodawaj tych reguł wyłącznie na podstawie nazw kolumn.

Opis bazy zawiera zapis `ExpenceShare`; aktualna klasa to `ExpenseShare`.
Różnicę schematu tej tabeli między SQL inicjalizacyjnym a modelem wyjaśnia
[status implementacji](implementation-status.md).

## Źródła

- [Słownik pojęć](../../doc/Słownik%20pojęć.txt)
- [Opis tabel i atrybutów](../../doc/Baza%20danych.txt)
- [Model konceptualny](../../doc/Model%20konceptualny.png) i [logiczny](../../doc/Model%20logiczny.png)
- [Podział encji na moduły](../../doc/Podział%20encji%20na%20moduły.png)
- [Aktualne moduły, modele i konfiguracje](../backend/ZPI4M.Backend/src/modules/)
- [Wymagania funkcjonalne](../../doc/Funkcjonalności.txt)
