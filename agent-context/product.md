# Produkt i wymagania 4Money

Czytaj przy ustalaniu zachowania funkcji, zakresu MVP, UI oraz prac nad OCR,
kategoryzacją i integracjami. Stan wykonania opisuje [status](implementation-status.md).

Zakres porównano z tekstami i roadmapą w lokalnym `ZPI/doc` dnia 2026-10-21.
Tekstowe źródła wymagań, w tym aktualne `Funkcjonalności.txt`, ponownie
sprawdzono dnia 2026-10-23, uwzględniając aktualizację `Baza danych.txt`
o dezaktywację konta. `Funkcjonalności.txt` i `Baza danych.txt` ponownie sprawdzono 2026-10-27;
poniższy zakres uwzględnia aktualne oznaczenia MVP i rozszerzenia.
Diagramy architektury, modelu konceptualnego, logicznego i podziału encji
ponownie sprawdzono 2026-10-27. Roadmapa zachowuje datę wcześniejszego przeglądu.
Katalog ten znajduje się poza repozytorium, pod `../doc` względem jego katalogu
głównego; poniższe odnośniki są względne względem plików w `agent-context`.

## Cel i odbiorcy

4Money (For Money, 4M) ogranicza ręczną pracę przy rejestrowaniu, klasyfikacji
i analizie wydatków osobistych oraz współdzielonych. Odbiorcami są osoby
kontrolujące własny budżet, pary, rodziny, współlokatorzy i grupy wyjazdowe.

Wspólny model obejmuje wydatki wpisane ręcznie, odczytane z paragonu i pobrane
z banku. Analiza schodzi do poziomu produktów: jedna płatność w supermarkecie
może obejmować żywność, kosmetyki i chemię gospodarczą. Połączenie tego modelu
z paragonami, bankowością i udziałami użytkowników stanowi wyróżnik produktu.

## Reprezentatywny scenariusz

1. Użytkownik płaci 183,47 zł w supermarkecie; transakcja trafia do systemu z banku.
2. Skanuje paragon; system odczytuje sprzedawcę, datę, kwotę i pozycje.
3. System proponuje powiązanie z transakcją na podstawie kwoty, daty i sprzedawcy.
4. Użytkownik akceptuje lub odrzuca propozycję i poprawia wynik OCR lub kategorie.
5. Wskazuje uczestników i proporcje podziału kosztu całego wydatku.
6. System aktualizuje wykorzystanie limitów, raporty i salda zobowiązań.

To scenariusz docelowy, a nie opis obecnego przepływu działającego end-to-end.
Automatyczne dopasowanie paragonu do transakcji (kroki 3–4) jest obecnie
rozszerzeniem poza MVP.

## Zakres funkcjonalny i MVP

Sekcja 2 `Funkcjonalności.txt` nazywa się obecnie „Zakres funkcjonalny”.
Tylko podsekcje oznaczone `(MVP)` traktuj jako jawnie wskazany zakres MVP.

| Obszar | Wymagana możliwość | Oznaczenie w źródle |
| --- | --- | --- |
| Konto | Rejestracja, logowanie, wylogowanie, edycja danych i danych uwierzytelniających | 2.1, MVP |
| Wydatki | Ręczne dodawanie, historia, edycja, kategorie i wspólny model różnych źródeł | 2.2, MVP |
| Paragony | Zdjęcie w aplikacji mobilnej, upload zdjęcia lub zrzutu ekranu, OCR pozycji i ręczna korekta | 2.3, MVP |
| Kategoryzacja | Automatyczne propozycje dla wydatków i produktów z możliwością zmiany | 2.4, MVP |
| Bankowość | Automatyczny import transakcji; dopuszczalny dostawca API, sandbox lub symulator | 2.5, MVP |
| Limity | Definiowanie limitów, powiązanie z kategoriami, alerty przed przekroczeniem i po nim | 2.6, bez oznaczenia MVP |
| Grupy | Tworzenie, zaproszenia, wiele członkostw, przypisanie wydatku i powiadomienie członków o dodaniu wydatku | 2.7, MVP |
| Podział kosztów | Wskazanie uczestników całego wydatku, obliczenie udziałów i prezentacja wzajemnych zobowiązań | 2.8, MVP |
| Raporty | Wydatki w czasie, kategorie, wykorzystanie limitów, struktura zakupów, finanse osobiste i grupowe | 2.9, bez oznaczenia MVP |
| Powiadomienia | Wewnętrzna skrzynka powiadomień dotyczących konta użytkownika | 2.10, MVP |

Limity i raportowanie pozostają w zakresie funkcjonalnym; brak oznaczenia MVP
nie oznacza usunięcia ich z projektu. Automatyczne dopasowanie przeniesiono
do sekcji 3, mimo pozostawienia historyjki użytkownika 1.6.

MVP obejmuje podział na poziomie całego wydatku (sekcje 1.9 i 2.8
`Funkcjonalności.txt`). Przypisywanie uczestników do poszczególnych produktów
w ramach jednego wydatku jest rozszerzeniem poza MVP (sekcja 3).
Odczyt pozycji paragonu i kategoryzacja produktów nadal należą do MVP;
nie wymagają przez to rozliczania każdej pozycji między uczestnikami.

Rozliczenie finansowe odbywa się poza aplikacją. Dokumentacja nie określa jeszcze
wszystkich reguł naliczania udziałów, zaokrągleń ani progów alertów — nie traktuj
przypadkowo wybranych wartości jako uzgodnionych wymagań.

## Uzgodnione rozszerzenie konta

Opis bazy w `ZPI/doc/Baza danych.txt` uwzględnia już potwierdzenie e-maila
i `UserToken` z przeznaczeniami weryfikacji, zmiany e-maila, resetu hasła,
odświeżania sesji oraz dezaktywacji konta (purpose 5). Lista MVP
w `Funkcjonalności.txt` pozostaje bardziej ogólna. Bieżący kod realizuje
rejestrację i potwierdzanie e-maila, logowanie,
odświeżanie i wylogowanie, edycję profilu, zmianę e-maila, reset zapomnianego
hasła oraz dezaktywację konta po potwierdzeniu tokenem z e-maila. Dezaktywacja
ustawia IsActive = false i usuwa wszystkie tokeny konta; dane użytkownika
pozostają w bazie. Zmiana hasła po podaniu obecnego hasła pozostaje
niezaimplementowana; szczegóły i ograniczenia opisuje status implementacji.
Dezaktywacja jest uzgodnionym rozszerzeniem obsługi konta; aktualna lista MVP
w `Funkcjonalności.txt` nie wymienia jej jako osobnego punktu. Nie obejmuje
fizycznego usuwania danych ani reaktywacji konta.

Wysyłanie wiadomości odbywa się przez konfigurowalny adapter SMTP. Serwer może
należeć do zewnętrznego dostawcy, np. o2, albo być utrzymywany samodzielnie.
Nie zapisuj danych skrzynki w kodzie ani Git.
E-mail jest wymagany i unikalny; reset przez e-mail wymaga potwierdzonego adresu.
Te ustalenia uzupełniają dokumentację funkcjonalną; sama obecność wymagania
nie oznacza realizacji całego przepływu ani potwierdzonego wdrożenia.

## Powiadomienia

`Funkcjonalności.txt` wymaga powiadomienia członków grupy o dodaniu wydatku
(2.7, MVP) oraz alertów o zbliżaniu się do limitu i przekroczeniu limitu
(2.6, bez oznaczenia MVP). Nie określa progów ani częstotliwości alertów.
Wewnętrzna skrzynka jest wprost wymagana w 2.10 (MVP). Push zamiast samej
skrzynki oraz interaktywny push po wykryciu transakcji bankowej, z opcją „tak”
dodającą wydatek, są rozszerzeniami z sekcji 3; nie należą do bieżącego zakresu.

Aktualny zakres uzgodniony z użytkownikiem to inbox w aplikacji webowej
i mobilnej, ze stanem odczytu per użytkownik. Push odłożono do rozszerzeń;
nie planuj teraz tokenów urządzeń, integracji dostawcy ani kolejki wysyłki.
`Baza danych.txt` opisuje już NotificationRecipient z read_at oraz nowe pola
Notification. Dołączanie do grupy ma odbywać się kodem lub linkiem; uzgodniony
typ GroupMemberJoined informuje o dołączeniu, a nie stanowi zaproszenia push.
Serwis NotifyAsync i API inboxa (lista, odczyt pojedynczy i zbiorczy) są już
w kodzie. Nie są jeszcze podłączone przepływy generujące powiadomienia
w Grouping/Limits ani aplikacje klienckie; szczegóły opisuje status implementacji.

## Rozszerzenia i wyłączenia

Rozszerzenia poza MVP, zgodnie z sekcją 3 `Funkcjonalności.txt`:

- automatyczne dopasowanie transakcji bankowej do paragonu;
- powiadomienia push zamiast jedynie wewnętrznej skrzynki w aplikacji;
- wykrywanie transakcji bankowej i interaktywny push z pytaniem o dodanie wydatku
  (odpowiedź „tak” dodaje wydatek);
- przypisywanie uczestników do poszczególnych produktów w ramach jednego wydatku;
- przekierowanie do widoku przelewu bankowego z poziomu podziału wydatków;
- integracje z systemami e-paragonów;
- wdrożenie MCP.

Aktualny `Słownik pojęć.txt` (sprawdzony 2026-10-27) nie zawiera już pojęcia
prognozy. Prognozowanie wydatków nie jest wymienione w obecnym zakresie
funkcjonalnym ani rozszerzeniach; raportowanie trendów i alerty limitów
nie oznaczają wymagania przewidywania przyszłych wydatków.

Poza zakresem projektu są wykonywanie przelewów między użytkownikami,
przechowywanie środków, obsługa kart płatniczych, zastępowanie aplikacji bankowej
i doradztwo finansowe.

## Cele jakościowe

| Obszar | Cel projektowy |
| --- | --- |
| OCR | Co najmniej 90% poprawnie odczytanych pól liczbowych |
| OCR | Poprawna kwota do zapłaty i data zakupu dla co najmniej 95% paragonów |
| Kategoryzacja | Macro F1 co najmniej 0,8 dla zdefiniowanych kategorii |

Są to wymagania, a nie potwierdzone wyniki aktualnego pipeline. Repozytorium ma
już narzędzia ewaluacji, fixture'y i historyczne analizy eksperymentów
([instrukcja](../workers/extraction_worker/experiments/README.md),
[notatki](../notes/ocr/)). Przegląd statyczny z 2026-10-29 potwierdza implementację
OCR i propozycji kategorii w workerze Python; nie dowodzi spełnienia powyższych
progów przez aktualny domyślny model ani realizacji korekt użytkownika w aplikacji.
Przy ocenie jakości wskaż konkretny model, pipeline, zbiór i protokół pomiaru.

## Plan etapów

Roadmapa przewiduje kolejno: analizę i projekt systemu, rdzeń, odczyt
i klasyfikację paragonów, Open Banking, interfejs użytkownika, raporty i analizę
oraz integrację i finalizację. Nie zawiera terminów ani potwierdzenia ukończenia.

## Źródła

- [Wizja projektu](../../doc/Wizja%20projektu.txt)
- [Funkcjonalności i zakres MVP](../../doc/Funkcjonalności.txt)
- [Fiszka i cele jakościowe](../../doc/Fiszka%20projektowa.txt)
- [Słownik pojęć](../../doc/Słownik%20pojęć.txt)
- [Roadmapa](../../doc/4Money-Roadmap.jpg)
