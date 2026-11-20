# Domena i model danych

Właściciel pojęć, relacji i niezmienników wspólnych dla modułów. Zakres wymagań
opisuje [produkt](product.md), transakcje [architektura](architecture.md), a braki
[status](implementation-status.md). Szczegółowe DTO i reguły HTTP utrzymuj w
kontrakcie modułu, nie w tym zestawieniu. Poniższy model odnosi się do bieżącego
kodu i reguł biznesowych, nie do samego bazowego SQL init.

## Pojęcia i encje

| Pojęcie | Znaczenie i właściciel |
| --- | --- |
| Konto / User | Tożsamość i profil użytkownika aplikacji; User. Nie rachunek bankowy |
| UserToken | Skrót jednorazowego tokenu lub refresh tokenu sesji; User |
| Group | Zwykła przestrzeń współdzielenia kosztów, także osobistych; Grouping |
| GroupMember | Członkostwo, aktywność i agregaty płatności/udziałów/rozliczeń; Grouping |
| Expense | Zakup z płatnikiem, grupą i opcjonalnymi źródłami; Expense |
| ExpenseItem | Produkt lub usługa, kategoria, ilość, cena i rabat; Expense |
| Category | Słownik kategorii produktów, nie sprzedawców; Expense |
| ExpenseShare | Procentowy udział użytkownika w całym wydatku; Expense |
| Receipt | Prywatny obraz i oryginalny wynik OCR; Receipt |
| BankAccount | Rachunek bankowy użytkownika zidentyfikowany przez dostawcę; Banking |
| BankTransaction | Zaimportowana operacja bankowa, odrębna od wydatku; Banking |
| BankTransactionAssignment | Jedno wykorzystanie transakcji przez Expense lub GroupSettlement; Banking |
| BankConnectionAttempt / Connection | Próba autoryzacji i trwałe lokalne połączenie z bankiem; Banking |
| GroupSettlement | Zapis rozliczenia wykonanego poza aplikacją; Grouping |
| GroupInvitation | Ograniczone czasem/licznikiem zaproszenie z hashem tokenu; Grouping |
| UserLimit / GroupLimit | Definicja budżetu PLN właściciela albo grupy; Limits |
| Notification / Recipient | Treść i przypisanie odbiorcy z indywidualnym odczytem; Notifications |
| Raport | Agregacja źródeł; nie osobna encja utrwalonych wyników w obecnym modelu |

Wydatek, pozycja, paragon i transakcja nie są zamienne. Połączenie źródeł tego
samego zakupu nie powinno dublować kosztu. Tożsamość aktora jest osobną informacją
od płatnika, uczestnika udziału, właściciela rachunku czy odbiorcy powiadomienia.
Parametr `userId` w danych domenowych nie daje uprawnień do działania jako ta osoba.

## Wydatki i podział kosztów

Expense wymaga grupy oraz jednego `FkPayerId`; nie ma osobnego autora jako pola
modelu. Powiązania paragonu i transakcji są opcjonalne, najwyżej 1:1 w Expense.
Wydatki osobiste korzystają ze zwykłej grupy, także grupy startowej tworzonej dla
nowego konta. Nie ma oddzielnego typu ani specjalnych blokad „grupy osobistej”.

ExpenseShare ma klucz `(FkExpenseId, FkUserId)` i `Percentage numeric(5,2)`.
Uczestnicy są unikalni, procenty muszą dawać dokładnie 100. API utrwala procenty,
nie tryb formularza ani kwotowy udział. UI może przyjmować kwotę lub suwak, ale
musi przeliczyć wynik na kontrakt API. Podział produktów na uczestników nie
wynika z istnienia pozycji i nie należy do bieżącego modelu.

Kwoty udziałów oblicza Expense metodą największych reszt: rozdziela pozostałe
grosze według reszty, z deterministycznym remisem według UUID ordinal. Kwoty
przekazane do agregatów Grouping muszą sumować się do całej kwoty wydatku.
Nie stosuj niezależnego zaokrąglania każdej osoby, które zmienia sumę. Reguła
jest wspólna dla zapisów, odczytów wykorzystania i syntetycznego seeda.

Pozycja ma dodatnią ilość `numeric(18,3)`, cenę `numeric(18,2)` także zerową lub
ujemną, rabat kwotowy `Discount <= 0`, opcjonalne Unit i flagę IsSystemGenerated.
Rabat dotyczy całej pozycji; ujemny rabat zmniejsza jej wartość. Jeśli podano Amount, suma pozycji nie może go przekroczyć; różnica
jest zapisywana jako systemowe „Nieznane”. Puste zwykłe pozycje są dopuszczane tylko zgodnie z tą ścieżką.
Końcowa kwota wydatku musi być dodatnia. Kategorie ręcznych pozycji muszą istnieć;
systemowego „Nieznane” nie wybiera się jako zwykłej kategorii produktu.

`Version` jest wymaganym UUID i tokenem współbieżności, publicznie nieprzezroczystym
tekstem. PUT/DELETE wymagają bieżącej wersji; konflikt daje 409. Edycja odwraca
poprzedni wkład i stosuje nowy w jednej transakcji, zamiast ponownie dopisywać
cały koszt do sald. GET i preview nie rezerwują źródeł ani nie aktualizują sald.
Aktualne członkostwo, archiwum, płatnik, źródła i waluta są sprawdzane ponownie
przy zapisie pod blokadą. Implementacja Expense obsługuje PLN.

Category.Icon przechowuje nazwę Lucide jako text; kolor jest decyzją frontendu,
nie polem modelu. Słownik i systemowe ID są utrzymywane przez migracje, seed
zachowuje kategorie. Taksonomia workera i mapowanie .NET wymagają wspólnej
kontroli przy zmianie kategorii. „Kaucja” i „Zniżka” mają odrębne znaczenia;
frontend wymaga dla „Zniżki” ujemnej ceny, ilości 1 i rabatu 0. Ta reguła UI
nie jest ogólnym ograniczeniem backendu zależnym od kategorii.

Kontrakt formularza, uprawnień, filtrów i źródeł: [Expense API](../notes/EXPENSE_API.md).
Wewnętrzne odczyty raportowe opisują [Expense Reports](../notes/EXPENSE_REPORTS.md).

## Grupy, członkostwa i rozliczenia

Group ma administratora `AdminUserId`, walutę CurrencyCode oraz opcjonalne opis
i ikonę. Waluta ma trzy wielkie litery ASCII. `Icon` oznacza preset, a
`IconStorageKey` własny plik; oba nie mogą być ustawione jednocześnie. Usunięcie
grupy jest archiwizacją, zachowującą historię i pliki. Samo archiwum nie usuwa
wydatków, zobowiązań ani źródeł bankowych.

GroupMember ma klucz użytkownik/grupa, JoinedAt, IsMember oraz cztery nieujemne
agregaty `numeric(18,2)`: TotalPaid, TotalShare, TotalSettlementsSent,
TotalSettlementsReceived. `IsMember=false` oznacza zachowane byłe członkostwo.
Opuszczenie/usunięcie nie zeruje sald i nie jest banem; powrót może reaktywować
wpis z zachowaniem historii. Administrator przed odejściem przekazuje rolę.

Saldo do sugestii wynosi `Paid - Share + Sent - Received`. Poprawne agregaty
całej grupy mają sumę sald równą zero. CHECK nieujemności nie utrzymuje tej
własności: wkład trzeba aktualizować dokładnie raz razem ze źródłem. Błędna
historia wymaga uzgadniania, nie „naprawiania” odczytem albo zerowaniem licznika.
Sugestie obejmują także byłych członków, są deterministyczne i nie gwarantują
minimalnej liczby przelewów. Nie zapisują rozliczeń ani nie przesyłają pieniędzy.

GroupSettlement zapisuje różne osoby From/To, dodatnią Amount, Method
(Cash=1, BankTransfer=2, Other=3), czas UTC i opcjonalne BankTransactionId.
Obie strony mogą być historycznymi członkami; aktor musi mieć bieżące uprawnienia.
Data nie może być przyszła. Kwota nie jest ograniczana aktualnym saldem.
Nowe powiązanie bankowe jest obecnie odrzucane; nie traktuj kolumny i FK jako
ukończonego procesu przypisania. Odczyt historycznego bankowego ID zależy od
uprawnień aktora w Banking; administrator grupy nie zyskuje dostępu do cudzych
rachunków. Szczegóły: [Grouping](../backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/README.md).

GroupInvitation przechowuje hash tokenu, termin, MaxUses, UseCount i IsActive.
Wygaśnięcie i wyczerpanie licznika są niezależne od odwołania. Użytkownik będący
już członkiem nie zużywa ponownie zaproszenia. Akceptacja musi atomowo zmienić
licznik, członkostwo i powiadomienie; sam CHECK licznika nie realizuje tego procesu.

## Budżety

Limit to trwała definicja PLN, a nie rekord odnowionego okresu. Week=1 obejmuje
poniedziałek–niedzielę, Month=2 miesiąc kalendarzowy, Year=3 rok; wszystkie
według Europe/Warsaw. Custom=4 przechowuje skończone StartDate/EndDate, obie
włączne, z początkiem nie późniejszym od końca. Przy tworzeniu koniec nie może
być w przeszłości. Nie ma maksymalnej długości podróży, odnowienia ani następcy.

Kalendarzowe definicje mają NULL w zapisanych datach; odpowiedzi API zawsze
zwracają obie skończone daty wyliczonego okna. Custom zawsze zwraca zapisany
zakres, również po zakończeniu lub wyłączeniu. Właściciel, kategoria, waluta,
okres i daty są niezmienne. Kwota zmienia się od razu, lecz zakończony Custom
zamraża cel; korekty Expense nadal mogą zmienić jego wykorzystanie.

W jednym scope właściciel/kategoria/waluta może istnieć najwyżej jeden włączony
limit kalendarzowy (Week/Month/Year). Custom może współistnieć z nim i dowolnymi
innymi Custom, również o identycznych datach. Ogólny limit i kategoria są osobnymi
scope. Utworzenie/włączenie definicji nie wyłącza innych ani nie zmienia ich wersji.
Każdy pasujący limit niezależnie liczy wydatki i generuje alerty; tych wartości
nie należy sumować jako osobnych zakupów. Konflikt 409 dotyczy kalendarzowych.

Wykorzystanie osobiste sumuje udziały właściciela, grupowe zakupy grupy, a
kategoria ogranicza pozycje. Kalendarz liczy całe okno, także koszt sprzed
utworzenia definicji. PUT kwoty i PATCH enabled wymagają aktualnej Version.
Stan alertu jest osobny dla definicji i początku okresu; Custom używa swojego
początku. Zmiana kwoty/wyłączenie nie resetują historii. Zakończone i przyszłe
Custom nie wysyłają alertów. Szczegóły walidacji, progów i praw dostępu należą
do [Limits](../backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md).

## Bankowość i wyłączność źródeł

Rachunek należy do użytkownika. Import identyfikuje go parą TinkUserId/TinkAccountId;
numer, IBAN i instytucja są opcjonalne. Nie używaj numeru wyświetlanego jako
zastępstwa stabilnej tożsamości dostawcy. Stan próby połączenia, zgody,
rachunku i synchronizacji to odrębne stany; sukces autoryzacji nie dowodzi
zakończonego pobrania transakcji.

BankTransactionAssignment ma klucz transakcji i konsumenta Expense=1 albo
GroupSettlement=2. Zapewnia jednego konsumenta w rejestrze; osobne UNIQUE w
Expense i Settlement nie zapewniają wyłączności między tabelami. Każdy nowy
konsument musi atomowo utrzymywać rejestr ze swoim zapisem i zwalniać wyłącznie
własne przypisanie. Nie usuwaj cudzego przypisania przy konflikcie. Bieżącą
integrację konsumentów i kontrakt importu opisuje [Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md).

## Paragony i OCR

Receipt.StorageKey to wymagany, unikalny klucz obiektu, nie URL. ExtractionResult
jest oryginalnym JSON-em `receipt` / `warnings` / `needs_review`, utrwalonym jako
jsonb. Analiza pliku sama nie tworzy ExpenseItem ani Category. Zaakceptowany
formularz i oryginalny wynik są odrębne, a podpisany ocrProof wiąże wynik z
użytkownikiem i skrótem pliku. Zmiana formularza nie powinna fałszować oryginału.

Kwoty i ilości OCR są tekstami dziesiętnymi, brakujące dane null. Zachowywane
są odczytana nazwa i propozycja korekty, kategoria oraz flagi przeglądu.
Rabaty produktów, samodzielna zniżka, deposits, deposit_returns, total i
amount_due mają odrębne znaczenia. HTTP 200 nie znosi potrzeby przeglądu.
Dostęp do zapisanego paragonu jest autoryzowany przez Expense; seed nie uploaduje
obrazu dla syntetycznego klucza. API workera: [README](../workers/extraction_worker/README.md).

## Powiadomienia

Notification ma typ General=0, ExpenseAdded=1, LimitApproaching=2,
LimitExceeded=3, GroupMemberJoined=4, SettlementReceived=5, tytuł, treść i czas UTC.
Opcjonalnym celem jest wydatek albo grupa; NotifyAsync odrzuca jednoczesne oba
FK, ale baza nie ma odpowiadającego CHECK. Odbiorca ma klucz powiadomienie/użytkownik
i ReadAt; NULL oznacza nieodczytane. Odczyt jest indywidualny.

Nowe przypisania powstają wyłącznie dla istniejących aktywnych kont z lookupu
User. Pusta lista lub brak aktywnych odbiorców daje null bez zapisu; nie usuwa
historycznych powiadomień ani nie blokuje źródła biznesowego. Błąd zapisu jest
inną sytuacją i wycofuje wspólną transakcję. Usunięcie Expense ustawia jego FK
w powiadomieniu na NULL, zachowując historię. Grupowy alert wskazuje GroupId,
osobisty nie wskazuje grupy ani wydatku. Stan deduplikacji alertów należy do Limits,
nie do indywidualnego ReadAt. Inbox webowy nie oznacza dostarczania push.

## Konto i tokeny użytkownika

User oznacza tożsamość aplikacji; identyfikatory integracji nie są publicznym
profilem. Login jest unikalny, e-mail unikalny wyłącznie dla aktywnych kont.
GoogleSubject jest opcjonalny i unikalny; konto Google może nie mieć hasła.
Dezaktywacja zachowuje dane finansowe i usuwa tokeny; nie zwalnia loginu.

UserToken przechowuje hash, CreatedAt, ExpiresAt, Purpose i warunkowy TargetEmail.
Purpose: potwierdzenie=1, zmiana adresu=2, reset hasła=3, refresh=4,
dezaktywacja=5. UNIQUE użytkownik/purpose dopuszcza jedną sesję konta i jeden
bieżący token danego przepływu. Dla 1–3 i 5 TargetEmail jest wymagany, dla
refresh NULL. JWT nie jest zapisywany; sub wskazuje konto, sid sesję. Refresh
rotuje token bez przedłużenia bezwzględnego terminu sesji. Nowy login zastępuje
sesję; zmiana e-maila, reset hasła i dezaktywacja unieważniają wszystkie tokeny.
Procedury, wyjątki Google i zachowanie SMTP: [User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md).

## Daty, kwoty i ograniczenia schematu

Pieniądze to decimal/numeric(18,2), ilości numeric(18,3); nie używaj float do
podziału groszy. API może serializować liczby jako tekst, ale nie zmienia to
reguł obliczeń. Timestamps zdarzeń są UTC/timestamptz. BirthDate i data transakcji
bankowej są DateOnly/date. Expense.TransactionTime przechowuje timestamptz,
lecz HTTP przyjmuje YYYY-MM-DD, a nowe zapisy północ UTC. Raporty Expense mają
obie daty włączne; historia HTTP zachowuje koniec wyłączny. Nie przenoś jednej
konwencji do drugiej bez konwersji. Daty zakupów dla Reports i Limits wynikają z UTC Expense, także dla starszych
rekordów z godziną. Europe/Warsaw określa „dzisiaj” i bieżące okno budżetu.

SQL init nie jest końcowym modelem. Część FK istnieje w SQL/migracjach bez
mapowania EF, m.in. Expense do Group/Receipt/BankTransaction i ExpenseShare do
User. AppDbContext mapuje osobno płatnika oraz Notification do Expense. Sama
właściwość Fk... nie definiuje relacji. Przy zmianie porównuj konfiguracje,
snapshot, migracje i SQL, aby nie zgubić nieśledzonych ograniczeń.

Źródła wymagań: [słownik](../../doc/Słownik%20pojęć.txt), [opis bazy](../../doc/Baza%20danych.txt),
[model konceptualny](../../doc/Model%20konceptualny.png), [logiczny](../../doc/Model%20logiczny.png)
i [podział encji](../../doc/Podział%20encji%20na%20moduły.png). To zewnętrzne materiały,
nie dowód aktualnego wdrożenia. Decyzje dotyczące nowej waluty, podziału produktów
lub nowego procesu bankowego wymagają osobnego ustalenia; nie wynikają z nazw tabel.
