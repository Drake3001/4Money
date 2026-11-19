# Expense — ustalenia produktowe i podstawa kontraktu API

Data zebrania ustaleń: **2026-11-01**. Doprecyzowanie kontraktu: **2026-11-02**.

## 1. Cel, status i sposób czytania

Ten dokument jest wspólnym źródłem uzgodnionych decyzji dotyczących Expense.
Ma służyć zespołowi oraz agentom projektującym kontrakty, modele, migracje,
frontend i implementację. Nie jest dokumentacją działającego API ani potwierdzeniem
ukończenia opisanych funkcji. Uzgodniony kontrakt 10 endpointów i DTO jest w
[Expense API](EXPENSE_API.md) oraz [OpenAPI 3.0.3](EXPENSE_API.openapi.yaml).
Stan wdrożenia operacji, integracji i migracji opisuje
[status implementacji](../agent-context/implementation-status.md).
Doprecyzowania z 2026-11-02 zastępują starsze otwarte
kwestie wyłącznie w opisanym niżej zakresie.

Źródłem są odpowiedzi zespołu na 76 pytań o Expense, dodatkowe pytanie o osoby
bez konta, odpowiedzi na 10 pytań uzupełniających oraz końcowe doprecyzowanie
użytkownika z 2026-11-01 dotyczące kwoty wydatku i pozycji „Nieznane”.
Treść jest samodzielna: do jej wykorzystania nie potrzeba załączników z rozmowy.

- **Ustalenia** w sekcjach 2–14 opisują zaakceptowane zachowanie produktu.
- **Konsekwencje dla implementacji** w sekcji 15 wskazują potrzebne prace;
  nie narzucają nieuzgodnionego projektu tabel lub endpointów.
- **Kwestie otwarte i rozstrzygnięcia** w sekcji 16 wskazują stan każdej decyzji.
  Nie wolno traktować wcześniejszych rekomendacji asystenta jako decyzji zespołu.
- Przy sprzeczności wcześniejszej i późniejszej odpowiedzi obowiązuje późniejsza.
  Najważniejsze zastąpione ustalenia wymienia sekcja 17.

Dokument uszczegóławia [produkt](../agent-context/product.md),
[domenę](../agent-context/domain.md) i [architekturę](../agent-context/architecture.md).
W zakresie opisanych tu decyzji Expense zastępuje wcześniejsze ogólne zapisy
o braku ustaleń. Faktyczny stan wykonania nadal opisują kod i
[status implementacji](../agent-context/implementation-status.md).
Nowe decyzje należy aktualizować tutaj; w innych dokumentach odsyłać do tego miejsca.

## 2. Zakres i granice MVP

1. Projektujemy pełny uzgodniony zakres Expense, aby frontend mógł rozpocząć pracę
   na stabilnym kontrakcie przed ukończeniem wszystkich serwisów backendu.
2. Zakres obejmuje ręczne tworzenie, historię, szczegóły, edycję, usuwanie,
   produkty i kategorie, podział kosztów, integrację z transakcjami bankowymi,
   paragony, wykorzystanie OCR oraz dane potrzebne do zobowiązań i raportów.
3. Expense wystawia frontendowi endpointy związane z wydatkiem i jego paragonem;
   przetwarzanie i przechowywanie paragonów realizuje przez serwisy Receipt.
4. Kontrakt ma obejmować cały zakres, a plan implementacji ma jawnie wskazywać
   kolejność realizacji. Konkretna kolejność nie została jeszcze ustalona.
5. Podział kosztów dotyczy całego wydatku. Podział uczestników per produkt
   pozostaje poza MVP; `FkUserId` jest usuwane z ExpenseItem.
6. Zwroty zakupów jako osobny proces, wiele walut, wielu płatników jednego
   wydatku, osoby bez konta, automatyczne dopasowanie źródeł, scalanie wydatków,
   historia zmian, kosz i praca offline nie należą do tego MVP.
7. Aplikacja ewidencjonuje przekazanie pieniędzy poza systemem. Nie wykonuje
   przelewów i nie przechowuje środków.
8. Nie wystawiamy endpointu summary w Expense; podsumowania dla frontendu należą
   do Reports. Dostarczenie danych dla Reports jest wymaganiem integracyjnym Expense;
   nie oznacza ukończenia modułu Reports ani zmiany ogólnego oznaczenia raportów
   w dokumentacji produktu na MVP.

## 3. Grupy, własność i wydatki osobiste

- Każdy wydatek należy do dokładnie jednej grupy. Nie ma wydatków bez grupy.
- Każdy użytkownik ma własną jednoosobową grupę na wydatki osobiste, roboczo
  nazywaną „Osobiste”. Nie wprowadzamy odrębnego modelu prywatnego wydatku.
- Nie przenosimy wydatku między grupami, także między osobistą a współdzieloną.
  Użytkownik może usunąć wpis i utworzyć nowy w innej grupie.
- Wydatek należy do grupy. Zapisujemy jednego płatnika, bez osobnego autora
  wpisu; płatnik nie otrzymuje przez to szczególnego prawa własności.
- W MVP płatnicy i uczestnicy mają konta aplikacji i należą do danej grupy.
  Tworzenie zastępczych osób bez konta zostało wycofane z MVP.

## 4. Członkostwo, widoczność i uprawnienia

| Osoba | Dostęp do wydatków i paragonów | Zapis |
| --- | --- | --- |
| Aktualny członek grupy | Wszystkie wydatki grupy i ich produkty oraz paragony, również sprzed dołączenia i bez własnego udziału | Może dodawać, edytować i usuwać wydatki grupy |
| Były członek będący płatnikiem lub uczestnikiem danego wydatku | Odczyt tego wydatku i jego paragonu | Bez edycji |
| Były członek bez powiązania z danym wydatkiem | Nie uzgodniono dodatkowego dostępu po odejściu | Brak podstaw do nadania prawa zapisu |

- Dodawanie i edycja nie są zarezerwowane dla administratora ani autora.
- Nie ma osobnej akceptacji uczestników przy przypisaniu kosztu.
- Po odejściu z grupy osoba pozostaje w dawnych wydatkach i udziałach.
  Odejście nie usuwa zobowiązań i nie daje możliwości przypisywania tej osoby
  do nowych wydatków.
- Zachowanie historii wymaga trwałej informacji o dawnym członkostwie.
  Rozważano oznaczanie członkostwa jako nieaktywnego; projekt tabel pozostaje
  do wykonania. **Odejście z grupy nie dezaktywuje konta User.**
- Dezaktywacja konta nie ma usuwać historycznych danych finansowych. Dostęp
  do aplikacji podlega istniejącym regułom uwierzytelniania User.
- Aktualny członek grupy może usunąć wydatek, bez wymogu bycia administratorem
  lub płatnikiem (2026-11-02). Byli płatnicy/uczestnicy mają tylko odczyt.
- Aktualny członek może zmienić lub usunąć istniejący udział byłego członka.
  Nie może dodać byłego członka jako nowego uczestnika. Istniejącego byłego
  płatnika można zachować; nowy płatnik musi być aktualnym członkiem.

## 5. Dane wydatku, waluta i czas

- Punktem wyjścia formularza są obecne dane: nazwa, opcjonalny sprzedawca,
  kwota, data i godzina, grupa oraz opcjonalne źródła bankowe i paragon.
  Dochodzą płatnik i podział kosztów.
- Nie uzgodniono dodatkowego opisu ani notatki. Dokładna wymagalność,
  długości i domyślne wartości pól zostaną opisane w kontrakcie.
- MVP obsługuje tylko PLN, bez przeliczania walut.
- Ustalenie z 2026-11-07: zakup ma tylko datę dzienną `YYYY-MM-DD`. Dozwolone są przyszłe daty.
- Formularz nie wymaga godziny. Filtry historii przyjmują daty: początek włącznie
  i koniec wyłącznie; filtry Banking pozostają obustronnie włączne.
- Ustalenie z 2026-11-08: wewnętrzny serwis raportowy przyjmuje obie granice dat
  włącznie, również zakres jednego dnia. Kontrakt historii HTTP pozostaje bez zmian.
- Baza zachowuje `timestamp with time zone`, bez migracji. Backend zapisuje północ UTC;
  API zwraca samą datę, również dla starszych rekordów. Szczegóły: [kontrakt API](EXPENSE_API.md).

## 6. Kwota, pozycje i systemowe „Nieznane”

### 6.1. Reguła spójności

`Expense.Amount` oznacza kwotę całego wydatku. Każdy zapisany wydatek ma pozycje:
produkty użytkownika albo automatyczną pozycję uzupełniającą.
Nie przypisujemy kategorii bezpośrednio całemu wydatkowi.

Niech `S` będzie sumą końcowych wartości zwykłych pozycji, po rabatach i innych
uzgodnionych korektach, bez automatycznego „Nieznane”. Dla ustalonego `Amount`:

- `S < Amount`: system uzupełnia różnicę pozycją „Nieznane”, w kategorii „Nieznane”.
- `S = Amount`: nie potrzeba dodatniej pozycji uzupełniającej.
- `S > Amount`: **zapis jest zabroniony**. Nie tworzymy ujemnego „Nieznane”.
- Brak produktów użytkownika: całą kwotę reprezentuje systemowe „Nieznane”.

Wynikowa suma pozycji wraz z uzupełnieniem musi być równa `Amount`.
Systemowa pozycja jest zarządzana automatycznie i nieedytowalna ręcznie.
Użytkownik wpisuje lub poprawia zwykłe produkty, a system przelicza dopełnienie.
Walidacja musi działać również w backendzie, niezależnie od formularza.

| Amount | Suma produktów | Wynik |
| --- | --- | --- |
| 100,00 zł | Brak | „Nieznane” 100,00 zł |
| 100,00 zł | 70,00 zł | „Nieznane” 30,00 zł |
| 100,00 zł | 100,00 zł | Brak dodatniego dopełnienia |
| Stałe 100,00 zł | 110,00 zł | Odrzucenie zapisu, bez ujemnego dopełnienia |

### 6.2. Ręczne tworzenie a źródło bankowe

- Przy normalnym ręcznym tworzeniu dodawanie produktów ma aktualizować `Amount`.
  Nie blokujemy takiego formularza na początkowej kwocie, jeżeli użytkownik
  buduje wydatek przez dodawanie pozycji.
- W wydatku z transakcji bankowej dodanie produktów **nie zwiększa automatycznie
  Amount**. Kwota ze źródła bankowego stanowi obowiązującą sumę; produkty ją
  objaśniają, a nie zmieniają.
- Przykład: przy ręcznym tworzeniu pozycje 40 zł i 70 zł dają kwotę 110 zł.
  Przy bankowym `Amount = 100 zł` te same pozycje przekraczają limit i zapis
  musi zostać odrzucony.
- Przy ręcznym tworzeniu brak `Amount` oznacza wyliczenie sumy produktów przez
  backend. Podane `Amount` jest sumą docelową; nadmiar produktów odrzucamy,
  niedobór uzupełniamy „Nieznane”. Wynik musi być dodatni.
- Przy niepełnej liście produktów względem zadanej kwoty działa „Nieznane”.
  Przy edycji z podanym Amount usunięcie produktu zwiększa dopełnienie,
  nie zmniejsza automatycznie całej kwoty. Usunięcie wszystkich produktów
  pozostawia pełne dodatnie Amount jako „Nieznane”. Pominięcie/null Amount
  także przy edycji oznacza wyliczenie z produktów, bez zachowania starej kwoty.
  Zerowego dopełnienia nie zapisujemy; wynik wydatku nadal musi być dodatni.
- Nie doliczamy systemowego „Nieznane” ponownie jako produktu budującego nową
  kwotę: jest wynikiem wyliczenia różnicy, nie niezależnym kosztem.

### 6.3. Zwykłe pozycje, rabaty i kaucje

- Pozycja ma nazwę, cenę jednostkową, ilość i kategorię.
- Ilość może być ułamkowa, np. 0,735. Darmowy produkt może mieć cenę 0.
- Rabat przypisany do pozycji jest kwotowy i dotyczy całej pozycji, nie jednej
  sztuki. `Discount` ma skalę 2, jest ujemny lub zerowy i domyślnie wynosi 0,
  zgodnie z konwencją OCR. Rabat nie może przekroczyć wartości produktu przed
  rabatem. Rabatu procentowego nie przyjęto do MVP.
- Kaucja i rabat obejmujący cały zakup mogą być osobnymi pozycjami.
  W pierwszej odpowiedzi wskazano roboczą kategorię „Zniżka” dla osobnego rabatu;
  nie zatwierdzono pełnego słownika kategorii ani jego identyfikatorów.
- `UnitPrice` dopuszcza wartości ujemne, zerowe i dodatnie; `Quantity` jest
  dodatnią ilością ze skalą 3. Opcjonalne `Unit` jest tekstem bez słownika.
- Wartość końcowa pozycji to `Round(UnitPrice × Quantity, 2, AwayFromZero)
  + Discount`; nie zapisujemy osobnych kolumn `BeforeDiscount` ani `Total`.
  Osobny rabat ma ujemne `UnitPrice` i `Discount = 0`. Nie liczymy rabatu dwukrotnie.
- `IsSystemGenerated` oznacza automatyczne dopełnienie „Nieznane”; nie
  rozpoznajemy go po nazwie. Istniejące pozycje otrzymują false. Samo pole
  nie implementuje tworzenia dopełnienia ani ochrony przed jego ręczną edycją.
- Zwykłe pozycje można dodawać, edytować i usuwać po zapisie, także gdy powstały
  z OCR. Po zmianie należy sprawdzić kwotę i przeliczyć udziały.

## 7. Kategorie i klasyfikacja

- Kategorie tworzą wspólny, płaski słownik aplikacji, bez kategorii prywatnych,
  grupowych i podkategorii.
- Użytkownik wybiera gotową kategorię. Nie tworzy, nie usuwa i nie zmienia jej
  nazwy ani ikony.
- Ikona kategorii to nazwa z biblioteki Lucide, przechowywana jako `text`.
  Kolor ustala frontend na podstawie motywu aplikacji; nie zapisujemy go w kategorii
  (doprecyzowanie 2026-11-02).
- „Inne” służy zwykłej pozycji bez konkretnej klasyfikacji. „Nieznane” służy
  systemowemu dopełnieniu brakującej kwoty. Nie utożsamiamy tych ról.
- Klasyfikacja w MVP działa przy OCR paragonu. Nie ma osobnej operacji
  automatycznego klasyfikowania ręcznie wpisanego wydatku.
- Wynik klasyfikacji od razu przypisuje kategorię, z możliwością ręcznej zmiany.
  Nie ma odrębnego stanu „proponowana kategoria” ani procesu jej akceptacji.
- Powyższe nie oznacza, że sam worker zapisuje Category lub ExpenseItem w .NET:
  mapowanie wyniku na model należy do integracji.

## 8. Płatnik, uczestnicy i podział kosztów

- Jest jeden płatnik całego wydatku. Osoba dodająca wpis może wskazać innego
  płatnika; nie zapisujemy jej jako osobnego autora wydatku.
- Płatnik nie musi mieć udziału w koszcie, np. kupił bilety wyłącznie innym.
- Domyślnie formularz wybiera jako uczestnika tylko płatnika. Można dodać innych
  członków grupy. To domyślne zachowanie formularza, nie zakaz innych podziałów.
- Obsługujemy cztery tryby: po równo, konkretne kwoty, procenty, proporcje
  (np. 2:1:1). Proporcje dotyczą całego kosztu, nie przypisania produktów.
- Suma końcowych udziałów musi pokrywać całą kwotę wydatku. Osoby z zerowym
  procentem pomijamy. Dodatni procent pozostaje zapisany także wtedy, gdy po
  rozdzieleniu groszy wynikowa kwota udziału wynosi 0,00 zł.
- Podział nie wymaga akceptacji osób, którym przypisano koszt.
- W MVP zapisujemy tylko procenty w ExpenseShare (`decimal(5,2)`). Tryby
  formularza mogą przeliczać dane na procenty; nie zapisujemy pierwotnego trybu,
  kwot ani proporcji, więc nie odtworzymy ich przy ponownym otwarciu formularza.
  Request API przyjmuje wyłącznie procenty; frontend przelicza pozostałe tryby.
  Zaokrąglenie procentów może zmienić wejściowe kwoty; użytkownik widzi wynik
  preview/backendu, którego końcowe kwoty zawsze sumują się do Amount.
- Zmiana kwoty automatycznie przelicza udziały, bez osobnego zatwierdzania
  przez uczestników. Dla podziału kwotowego zachowujemy proporcje poprzednich kwot:
  30 zł / 70 zł po zmianie sumy na 120 zł staje się 36 zł / 84 zł.
- Grosze rozdzielamy deterministycznie metodą największych reszt; remisy
  rozstrzygamy rosnąco po tekście GUID użytkownika (porządek ordinal).
- Nie ma stanu „rozliczony wydatek”, który blokowałby późniejsze korekty.

## 9. Transakcje bankowe

- Wydatek może mieć najwyżej jedną transakcję. Jedna transakcja może należeć
  najwyżej do jednego wydatku, także między różnymi grupami.
- Nie dzielimy transakcji między wiele wydatków. Import Banking sam nie tworzy
  Expense; utworzenie wpisu i powiązania koordynuje Expense.
- Użytkownik może przypisać wyłącznie swoją transakcję, będącą zaksięgowanym
  obciążeniem. MVP Expense obejmuje PLN; nie stosujemy konwersji innych walut.
- W Banking obciążenie ma znak ujemny, a wydatek dodatni: np. bankowe −100 zł
  odpowiada zakupowi o kwocie 100 zł. Nie zmieniamy oryginalnego zapisu bankowego.
- Dopóki bank jest przypięty, Amount odpowiada bezwzględnej kwocie obciążenia,
  a płatnik jego właścicielowi; oba pola są zablokowane (2026-11-02).
  Niespójne dane requestu są odrzucane. Swobodna zmiana wymaga odłączenia banku.
- Powiązanie nadajemy przez bankTransactionId przy POST Expense. Zachowanie,
  wymiana i odłączenie przez pełny PUT; null oznacza odłączenie. Aktualny członek
  może zachować już przypiętą transakcję innego członka, bez dostępu do prywatnego
  Banking. Nowo wybierana transakcja musi należeć do wywołującego.
- Lista dostępnych transakcji należy do Expense; wyklucza przypisane źródła
  przed paginacją i totalCount. Sam wybór/preview nie rezerwuje transakcji.
- Przy zamianie źródła użytkownik ma świadomie zdecydować, co zrobić z polami
  wydatku, i potwierdzić operację mogącą zmienić kwotę. Nie nadpisywać danych
  bez jawnego wyboru.
- Dane bankowe mają pierwszeństwo przed sprzecznym wynikiem OCR. Nie oznacza to,
  że bank dostarcza wszystkie pola, np. godzinę albo wiarygodną nazwę sprzedawcy.
- Dodanie produktów nie podnosi kwoty wydatku bankowego — obowiązuje sekcja 6.
  Wcześniejsza ogólna zgoda na edycję pól nie jest zgodą na automatyczne
  przekraczanie kwoty bankowej.
- Lista do przypisania pokazuje domyślnie transakcje nieprzypisane do żadnego
  wydatku. Przypisanie ustala Expense; ogólna historia Banking to inny widok.
- Członkowie grupy widzą dane wydatku, ale nie uzyskują dostępu do prywatnych
  szczegółów źródłowej transakcji, rachunku ani historii bankowej płatnika.
- Nie ma automatycznych propozycji dopasowania, rankingów ani zapamiętywania
  odrzuconych kandydatów. Użytkownik wybiera transakcję z listy.
- Nie wykrywamy dwóch wpisów opisujących ten sam zakup i nie scalamy ich.
  Użytkownik usuwa zbędny wpis i podpina źródło do pozostałego, o ile jest dostępne.

## 10. Paragony i OCR

### 10.1. Tworzenie i odczyt

- Można rozpocząć tworzenie wydatku od paragonu albo dołączyć go później.
- Jeden wydatek ma najwyżej jeden paragon; paragon nie może należeć do kilku
  wydatków. Wiele obrazów jednego paragonu to inne zagadnienie niż wiele paragonów.
- Receipt dostarcza wynik odczytu z klasyfikacją oraz surowy JSON. Expense
  przygotowuje z niego dane do formularza, które użytkownik przegląda i zatwierdza.
- Oryginalny JSON OCR pozostaje niezmieniony podczas ręcznej edycji wydatku.
  Można ponownie zastosować pierwotny wynik, aby odtworzyć dane zakupowe.
- Formularz przechowuje plik lokalnie. Analiza pliku zwraca propozycję i
  podpisany wynik powiązany z użytkownikiem i plikiem. Zapis z paragonem
  ponownie przesyła plik i opcjonalny podpisany wynik. Rekord Receipt i
  powiązanie powstają w transakcji zapisu Expense.
- Niepełny użyteczny wynik pozwala wykorzystać to, co odczytano, i uzupełnić
  dane ręcznie. Ostrzeżenia z JSON mają być widoczne dla użytkownika.
- Błąd uniemożliwiający użycie paragonu kończy się czytelnym komunikatem;
  nie zapisujemy go jako poprawnie przetworzonego wydatku.
- Osoby uprawnione do odczytu wydatku widzą jego produkty i obraz paragonu,
  w tym byli płatnicy/uczestnicy na zasadach sekcji 4.

### 10.2. Świadome zastosowanie wyniku

| Wariant | Zachowanie |
| --- | --- |
| Tylko obraz | Dołączenie paragonu bez zmiany danych zakupowych, kwoty, produktów i podziału; użytkownik może preferować ręczny wpis |
| Nadpisz | Zastąpienie danych zakupowych i pozycji wynikiem OCR; zachowanie grupy, płatnika i uczestników; ponowne przeliczenie udziałów |
| Dopisz | Zachowanie dotychczasowych pozycji i dodanie wszystkich pozycji OCR, bez zgadywania duplikatów |

- Ponowny OCR ani ponowne zastosowanie JSON nie nadpisują ręcznych korekt
  automatycznie. Wymagany jest świadomy wybór wariantu.
- Przywrócenie danych OCR nie oznacza przywrócenia całej historii wydatku:
  nie odtwarza dawnego członkostwa, płatnika ani starych udziałów.
- Każdy wariant zmieniający dane podlega regułom kwoty, „Nieznane”, udziałów
  i pierwszeństwa banku. Dopisanie nie daje prawa do przekroczenia bankowego Amount.
- PUT wymienia paragon po przesłaniu nowego pliku lub odpina go po ustawieniu
  `removeReceipt: true`. Zwykły PUT zachowuje paragon. Odpięcie nie usuwa
  pozycji wprowadzonych na podstawie OCR.
- Odczyt paragonu przez ID wydatku dziedziczy uprawnienia Expense. Przywrócenie
  danych korzysta z jednego oryginalnego wyniku zapisanego przy danym zdjęciu.
  Anulowanie formularza odrzuca lokalny plik i proof.
- Wymiana, odpięcie i usunięcie Expense kasują stary rekord Receipt w tej
  samej transakcji. Plik usuwamy po commicie. Trwałe ponawianie po awarii
  storage pozostaje osobnym zadaniem.

### 10.3. Pliki i ograniczenia

Zespół wskazał możliwości OCR: JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP i PDF;
maksymalnie 4 strony, 20 MiB na plik, 50 MiB na żądanie oraz limity pikseli.
Limity mają być konfigurowalne przez środowisko.

Nie jest to potwierdzenie gotowego uploadu tych wariantów przez Expense/Receipt.
Obecny adapter Receipt wysyła pojedynczy plik; obsługa wielu zdjęć przez worker
nie oznacza obsługi wielu zdjęć przez całą aplikację. Kontrakt musi jasno opisać
faktycznie udostępniony zakres i konfigurację limitów.

## 11. Płatności między użytkownikami i zobowiązania

- „Płatność” oznacza zapis faktu przekazania kwoty od jednej osoby do drugiej
  poza aplikacją, np. 100 zł gotówką. To nie operacja bankowa wykonywana przez 4Money.
- Nie jest przypisana do konkretnego Expense, produktu ani konkretnego długu.
  Może pokrywać część wzajemnych zobowiązań; kwota nie musi odpowiadać jednemu zakupowi.
- Płatności nie mają kategorii i nie wliczają się do wydatków ani raportów zakupowych.
  Są wejściem do obliczania tego, kto komu ile jest winien.
- Wydatki i płatności są osobnymi pojęciami. Płatności między użytkownikami
  realizuje Grouping; jego zespół ustala model persystencji.
- Edycja lub usunięcie wydatku nadal jest możliwe po zarejestrowaniu płatności.
  Nie istnieje flaga „rozliczony wydatek”; saldo ma uwzględniać aktualne dane
  wydatków oraz niezależne płatności.
- System ma upraszczać zobowiązania i ograniczać liczbę potrzebnych przekazań.
  Zespół wskazał cel najmniejszej liczby transakcji. Nie wybrano algorytmu ani
  nie ustalono, czy wymagana jest gwarancja globalnego minimum.
- Równoległy widok rzeczywistych relacji „kto za kogo zapłacił” obok uproszczonego
  planu pozostaje rozważaną możliwością, nie zatwierdzoną funkcją.
- Formalna obsługa zwrotów ze sklepu jest poza MVP. Na razie użytkownik może
  skorygować lub usunąć wydatek, np. usunąć zwrócony produkt. Nie dodajemy osobnych
  zwrotów bankowych ani procesu reklamacji. Przy korekcie nadal obowiązują reguły
  kwoty; ograniczenia wydatku powiązanego z bankiem opisuje sekcja 16.

## 12. Historia, filtry, szczegóły i raporty

- Potrzebne są listy: wydatki użytkownika we wszystkich grupach, wydatki grupy
  oraz wydatki konkretnego użytkownika w konkretnej grupie.
- „Wydatki użytkownika” oznaczają wydatki, do których jest przypisany jako
  uczestnik kosztu. To nie jest automatycznie lista wszystkich widocznych zakupów
  ani wyłącznie zakupów opłaconych przez tę osobę.
- Filtrowanie po osobie nie zastępuje autoryzacji. Nie daje prawa odczytu cudzych
  prywatnych wydatków ani grup, do których wywołujący nie ma dostępu.
- Lista ma dostarczać informacje potrzebne frontendowi: nazwę, sprzedawcę,
  kwotę, datę, grupę, płatnika, własny udział, kategorie i oznaczenia źródeł.
  Ostateczny zestaw pól zostanie zatwierdzony przy projektowaniu DTO z frontendem;
  nie należy wystawiać wszystkich pól encji i danych prywatnych „na zapas”.
- Szczegóły zwracają od razu wszystkie pozycje i udziały.
- Filtry obejmują grupę (w tym osobistą), użytkownika/uczestnika, płatnika,
  zakres dat, kwotę, kategorię, tekst i obecność paragonu/transakcji bankowej.
- Kategoria wybiera wydatki zawierające co najmniej jedną pozycję tej kategorii.
  Wydatek w wyniku pozostaje całym wydatkiem; filtr nie zmienia jego kwoty.
- Wyszukiwanie tekstowe dotyczy nazwy wydatku i sprzedawcy. Wyszukiwania
  produktów nie ma w MVP.
- Domyślne sortowanie: od najnowszych. Dodatkowo kwota i nazwa.
- Podsumowania dla frontendu należą do Reports, bez endpointu summary w Expense.
  Nie są liczone w odpowiedzi listy wyszukiwania. Nie mylić sum zakupów,
  udziałów i kwot zapłaconych.
- Expense ma udostępnić publiczne funkcje/serwis odczytu dla Reports: listy
  i sumy dla użytkownika we wszystkich grupach i we wskazanej grupie oraz dane
  z podziałem na kategorie. Nie ograniczać tego do endpointów dla frontendu.
- Płatności między osobami nie wchodzą do tych raportów.
- Historyczne ustalenie z 2026-11-08: starszy serwis zwracał listę, sumy, kategorie
  oraz okresy dzienne/miesięczne/roczne. Osobno liczy wartość zakupów, udział
  użytkownika i kwotę przez niego zapłaconą. Kategorie wynikają z pozycji po
  rabacie, z uwzględnieniem ujemnych pozycji i systemowego „Nieznane”. Koszt
  użytkownika w kategorii jest proporcjonalny do jego udziału w całym wydatku;
  grosze kategorii są uzgadniane do rzeczywistej kwoty tego udziału.
- Filtr kategorii pozostawia cały wydatek na liście; agregaty raportowe sumują
  wyłącznie wskazaną kategorię. Ten kontrakt odczytu zastąpiły cztery agregacje SQL;
  nieużywany serwis i jego DTO usunięto 2026-11-18. Bieżący kontrakt:
  [Expense dla Reports](EXPENSE_REPORTS.md).

## 13. Zapis, współbieżność, usuwanie i powiadomienia

- Formularz ma jeden przycisk zapisu. Zmiany wydatku, pozycji i udziałów zapisują
  się atomowo; nie zatwierdzamy części poprawnego formularza po błędzie reszty.
- Jeśli ktoś zapisze nowszą wersję, druga osoba ma otrzymać informację o konflikcie
  i odświeżyć dane. Nie nadpisujemy po cichu cudzej zmiany.
- Nie potrzebujemy historii zmian. Data ostatniej modyfikacji nie jest osobnym
  wymaganiem produktowym; techniczna wersja do kontroli współbieżności jest
  odrębną potrzebą i nie oznacza dodania historii.
- Kontrakt zwraca nieprzezroczyste version, canEdit i canDelete. PUT wymaga
  version w body, DELETE w query; nieaktualny token daje 409. Preview może
  sprawdzać tworzenie lub edycję, bez zapisu i rezerwacji źródeł.
- Usunięcie jest trwałe, bez kosza, z ostrzeżeniem w interfejsie. Obejmuje
  paragon i jego obraz. Usunąć może aktualny członek grupy (2026-11-02).
- Doprecyzowanie z 2026-11-08: bieżący zakres obejmuje wyłącznie powiadomienie
  ExpenseAdded po utworzeniu wydatku. Edycja i usunięcie nie powiadamiają.
  Kanałem jest istniejący inbox.
- Odbiorcy to wszyscy aktualni członkowie grupy poza osobą wykonującą operację,
  także jeśli nie są płatnikiem ani uczestnikami kosztu. Nie wykluczamy innego
  płatnika. Byli członkowie i nieaktywne konta nie otrzymują powiadomienia.
  Brak innych aktywnych odbiorców pozwala zapisać wydatek bez powiadomienia.
- Powiadomienie ma tytuł „Nowy wydatek”, nazwę wydatku, kwotę w PLN i nazwę grupy
  w treści oraz wskazuje ExpenseId. Po późniejszym usunięciu wydatku zachowuje
  treść i stan odczytu, a link zostaje wyzerowany przez istniejący FK SET NULL.
- Wydatek, agregaty i powiadomienie zapisują się we wspólnej transakcji;
  błąd powiadomienia wycofuje całą operację.
- Cała aplikacja wymaga połączenia. Ochronę przed duplikatami przy ponawianiu
  zapisu odłożono do późniejszego etapu; obecny zapis jej nie gwarantuje.

## 14. Granice modułów i istniejące konwencje

Ta sekcja odsyła do istniejących zasad repozytorium, nie stanowi nowej
decyzji produktowej ani potwierdzenia implementacji Expense.

- Zachowujemy modularny monolit i warstwy kontroler → serwis → repozytorium.
  Modele EF nie są publicznymi DTO.
- Tożsamość wywołującego pobieramy przez wspólny
  `ZPI4M.Shared.CurrentUser.ICurrentUser`, z istniejącym JWT; nie dodajemy
  modułowego adaptera ani wyboru właściciela danych przez niezaufane `userId`.
- Banking dostarcza odczyt przez `IBankingReadService`. Expense odpowiada za
  wykorzystanie i unikalność źródła oraz listę transakcji dostępnych do przypisania.
- Receipt realizuje OCR i magazyn plików. Expense musi egzekwować dostęp do
  powiązanego paragonu; sam obecny ReceiptService nie zapewnia tych uprawnień.
- Uzgodniony w architekturze przepływ powiadomień to Expense → Grouping →
  Notifications → User. Nie tworzymy cyklicznych zależności projektów.
- Zapis biznesowy i powiadomienie mają stosować
  [checklistę transakcji](../agent-context/architecture.md#checklista-zapisu-między-modułami).
  Wspólne połączenie nie rozpoczyna transakcji automatycznie.
- Atomowość bazy nie obejmuje automatycznie OCR ani S3. Błędy i sprzątanie
  plików wymagają jawnej koordynacji; pożądane usunięcie obrazu nie dowodzi,
  że obecny serwis gwarantuje fizyczne usunięcie przy każdej awarii.
- Przy projekcie HTTP korzystamy z istniejących wzorców `camelCase`, paginacji,
  kwot jako dokładnego tekstu dziesiętnego i ProblemDetails. Dokładne DTO,
  schematy i statusy opisuje [uzgodniony kontrakt](EXPENSE_API.md).
  Zachowujemy PagedQuery/PagedResult oraz sortBy/descending z istniejących modułów.
  Nie wprowadzamy własnej koperty błędów ani wymaganego pola code.

## 15. Konsekwencje dla implementacji i modeli

Statyczny przegląd obszaru Expense z 2026-11-01 potwierdza, że same obecne modele
nie realizują wszystkich powyższych wymagań. To nie jest raport z testów.

| Obszar | Co trzeba uwzględnić w projekcie implementacji |
| --- | --- |
| Expense | Jeden płatnik z FK do User, bez autora; tryb zarządzania kwotą i kontrola współbieżności pozostają do implementacji |
| ExpenseItem | Rozpoznanie systemowego „Nieznane”; rabat kwotowy; darmowe produkty; reprezentacja kaucji i osobnego rabatu |
| ExpenseShare | MVP zapisuje wyłącznie procenty, bez utrwalania trybu podziału |
| Grouping | Osobista grupa oraz zachowanie dawnego członkostwa i powiązań historycznych |
| Płatności | Przekazanie pieniędzy niezależne od zakupów i raportów; realizuje Grouping |
| Receipt | Etap przeglądu OCR, wariant tylko obraz, ponowne zastosowanie wyniku, sprzątanie i kontrola dostępu |
| Banking | Lista własnych dostępnych źródeł oraz walidacja przypisania także przy równoległych zapisach |
| Notifications | Wywołanie z procesu Expense/Grouping, polityka odbiorców i obsługa usunięcia powiązanego wydatku |
| Reports | Publiczny kontrakt odczytu Expense, bez wymuszania dostępu do repozytoriów/encji |

Zmiany modeli wymagają przeglądu konfiguracji, ograniczeń SQL, migracji i snapshotu.
Historyczne ograniczenia dodatniej ceny mogą kolidować z produktami za 0 zł
i rabatami. Nie zmieniać historycznego SQL ani zastosowanych migracji w miejsce
nowej migracji. Obecność `Amount` w Expense jest potwierdzona; wcześniejsza
wątpliwość zespołu o braku tego pola była nieaktualna.

## 16. Kwestie otwarte i rozstrzygnięcia z 2026-11-02

Nie blokują zebrania niniejszych ustaleń, ale muszą zostać rozstrzygnięte
w odpowiednim zadaniu przed zamrożeniem zależnego od nich fragmentu kontraktu.
Wiersze oznaczone jako rozstrzygnięte nie są już pytaniami otwartymi.

| ID | Kwestia |
| --- | --- |
| O1 | Rozstrzygnięte 2026-11-02: podane Amount pozostaje celem także przy edycji; brak/null wylicza sumę zwykłych produktów. Reguły i DTO w Expense API. |
| O2 | Rozstrzygnięte 2026-11-02: przypięty bank blokuje kwotę i płatnika; zmiana wymaga odłączenia. Osobny proces zwrotów nadal poza MVP. |
| O3 | Rozstrzygnięte 2026-11-02: wynik dodatni; brak zwykłych produktów przy podanym Amount daje pełne „Nieznane”; zerowej systemowej pozycji nie zapisujemy. |
| O4 | Rabat większy niż wartość produktu jest odrzucany. Jednostki pozostają tekstem; reguła remisów w podziale groszy: rosnąco po tekście GUID użytkownika (porządek ordinal). Normalizacja jednostek w formularzu pozostaje do ustalenia. |
| O5 | Usuwanie i korekty istniejących udziałów byłych członków rozstrzygnięte w sekcji 4. Ponowne dołączenie, ochrona i cykl życia grupy osobistej pozostają po stronie Grouping. |
| O6 | Płatności realizuje Grouping. Do ustalenia pozostają powiązanie z grupą, uprawnienia CRUD, potwierdzanie przez odbiorcę, nadpłaty i dostęp byłych członków. |
| O7 | Algorytm sald, gwarancja najmniejszej liczby przekazań i ewentualny drugi widok rzeczywistych relacji. |
| O8 | Trasy, przypinanie i blokada bankowa ustalone w Expense API. Nazwa, sprzedawca i data pochodzą z zatwierdzonego formularza; od 2026-11-07 data jest dzienna, bez godziny. Banking stosuje wykluczenia przypisanych transakcji przed paginacją i totalCount. |
| O9 | Analiza przed zapisem bez persystencji, ponowne przesłanie pliku przy zapisie i jeden oryginalny wynik OCR. Odczyt przez Expense. |
| O10 | Rozstrzygnięte: usunięcie/odłączenie/wymiana usuwa stary paragon po udanym zapisie; nie tworzymy biblioteki do ponownego użycia. |
| O11 | Agregaty i podsumowania dla frontendu należą do Reports; brak endpointu summary w kontrakcie Expense. Rozstrzygnięte 2026-11-08: koszt kategorii proporcjonalny do udziału w wydatku, z uzgodnieniem groszy. Bieżący IExpenseAggregationService i reguły filtrów opisuje EXPENSE_REPORTS.md; starszy odczyt usunięto 2026-11-18. |
| O12 | Rozstrzygnięte 2026-11-08: tylko ExpenseAdded, do aktualnych członków poza autorem operacji; szczegóły w sekcji 13. Po usunięciu wydatku treść zostaje, ExpenseId staje się null. Edycja/usunięcie nie tworzą nowych powiadomień. |
| O13 | Aktualizacja 2026-11-07: kontrakt 10 operacji, DTO, paginacja, wersja i statusy są w Expense API/OpenAPI oraz kontrolerach/DTO C#. Działają historia, szczegóły, preview, tworzenie, edycja i usuwanie oraz analiza i odczyt paragonu. Paragon zapisujemy razem z formularzem Expense. Grouping dostarcza rzeczywiste członkostwa, metadane i agregaty we wszystkich środowiskach. ExpenseAdded jest wysyłane przy tworzeniu według sekcji 13; edycja i usuwanie nie powiadamiają. Idempotency pozostaje odłożone. |

## 17. Ustalenia zastąpione i kontrola kompletności

| Wcześniejszy pomysł lub niejasność | Obowiązujące rozstrzygnięcie |
| --- | --- |
| Osoby bez konta w grupie i wydatku | Wycofane z MVP; uczestnicy mają konta |
| Prywatny wydatek bez grupy | Osobista grupa jednoosobowa |
| Wydatek bez pozycji lub kategoria całego wydatku | Zwykłe pozycje albo systemowe „Nieznane”; kategorie na pozycjach |
| Amount zawsze równe wyłącznie sumie produktów wpisanych ręcznie | Amount z dopełnieniem „Nieznane”; automatyka ręcznego tworzenia, stała kwota bankowa |
| Ujemne „Nieznane” przy nadmiarze pozycji | Zakaz zapisu powyżej ustalonego Amount |
| Były członek traci cały dostęp | Odczyt wydatków/paragonów, w których był płatnikiem lub uczestnikiem |
| Autor jest właścicielem wpisu | Wydatek należy do grupy; aktualni członkowie mogą go edytować |
| OCR daje propozycję kategorii wymagającą osobnego przyjęcia | Kategoria od razu przypisana i ręcznie korygowalna |
| Dopasowanie automatyczne i historia odrzuceń | W MVP ręczny wybór transakcji z listy |
| Spłata konkretnego wydatku i blokada „rozliczonego” wpisu | Niezależna płatność między osobami; brak blokady zmian wydatku |
| Osobny autor wydatku | Jeden płatnik; brak osobnego autora w modelu |
| Utrwalanie trybu podziału i jego danych wejściowych | MVP zapisuje wyłącznie procenty |
| Zwrot ze sklepu jako płatność między użytkownikami | Odrębne pojęcia; osobny proces zwrotów zakupów poza MVP |

Pokrycie pierwotnego kwestionariusza: zakres 1–3 → sekcje 2 i 10;
grupy 4–8 i dodatkowe pytanie o brak konta → 3–4; role 9–14 → 4 i 8;
dane 15–22 → 5–7 i 9–10; waluty/czas/zwroty 23–27 → 5 i 11;
podział 28–34 → 8; płatności/salda 35–38 → 11; kategorie 39–43 → 7;
bank 44–50 → 9; paragony 51–58 → 10; dopasowanie 59–63 → 9–10;
historia/raporty 64–70 i prośba Michała → 12; zapis 71–76 → 13.
Dziesięć odpowiedzi uzupełniających i końcowa reguła Amount są włączone
do odpowiednich sekcji, a pozostałe niejasności jawnie wymienia sekcja 16.

## 18. Odnośniki dla implementujących

- [Kontrakt Expense API](EXPENSE_API.md) i [OpenAPI 3.0.3](EXPENSE_API.openapi.yaml).
- [Produkt](../agent-context/product.md), [domena](../agent-context/domain.md),
  [architektura](../agent-context/architecture.md),
  [praca w repozytorium](../agent-context/development.md),
  [stan implementacji](../agent-context/implementation-status.md).
- [Moduł Expense](../backend/ZPI4M.Backend/src/modules/ZPI4M.Expense/),
  [Grouping](../backend/ZPI4M.Backend/src/modules/ZPI4M.Grouping/),
  [Receipt](../backend/ZPI4M.Backend/src/modules/ZPI4M.Receipt/).
- [Kontrakt Banking](../backend/ZPI4M.Backend/src/modules/ZPI4M.Banking/README.md),
  [worker OCR](../workers/extraction_worker/README.md).

Weryfikacja dokumentu: porównanie z odpowiedziami zespołu i końcowym
doprecyzowaniem użytkownika, przegląd wybranych modeli i kontekstu repozytorium
oraz kontrola lokalnych odnośników. Bez uruchamiania aplikacji, testów,
migracji, seeda i usług zewnętrznych.

Uzupełnienie 2026-11-02: porównanie zaakceptowanych decyzji z kontraktem API.
Walidacja specyfikacji nie stanowi uruchomienia endpointów, OCR ani storage.
