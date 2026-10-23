# Produkt i wymagania 4Money

Czytaj przy ustalaniu zachowania funkcji, zakresu MVP, UI oraz prac nad OCR,
kategoryzacją i integracjami. Stan wykonania opisuje [status](implementation-status.md).

Zakres porównano z tekstami i roadmapą w lokalnym `ZPI/doc` dnia 2026-10-21.
Tekstowe źródła wymagań, w tym aktualne `Funkcjonalności.txt`, ponownie
sprawdzono dnia 2026-10-23, uwzględniając aktualizację `Baza danych.txt`
o dezaktywację konta. Roadmapa i diagramy zachowują datę wcześniejszego przeglądu.
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

## Zakres MVP

| Obszar | Wymagana możliwość |
| --- | --- |
| Konto | Rejestracja, logowanie, wylogowanie, edycja danych i danych uwierzytelniających |
| Wydatki | Ręczne dodawanie, historia, edycja, kategorie i wspólny model różnych źródeł |
| Paragony | Zdjęcie w aplikacji mobilnej, upload zdjęcia lub zrzutu ekranu, OCR pozycji i ręczna korekta |
| Kategoryzacja | Automatyczne propozycje dla wydatków i produktów z możliwością zmiany |
| Bankowość | Automatyczny import transakcji; dopuszczalny dostawca API, sandbox lub symulator |
| Dopasowanie | Propozycja powiązania paragonu z transakcją, zatwierdzana lub odrzucana przez użytkownika |
| Limity | Definiowanie limitów, powiązanie z kategoriami, alerty przed przekroczeniem i po nim |
| Grupy | Tworzenie, zaproszenia, wiele członkostw, przypisanie wydatku i powiadomienie członków |
| Podział kosztów | Wskazanie uczestników całego wydatku, obliczenie udziałów i prezentacja wzajemnych zobowiązań |
| Raporty | Wydatki w czasie, kategorie, wykorzystanie limitów, struktura zakupów, finanse osobiste i grupowe |

MVP obejmuje podział na poziomie całego wydatku (sekcje 1.9 i 2.9
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
Nie planujemy historii powiadomień grupowych ani oznaczania ich jako przeczytane.
Te ustalenia uzupełniają dokumentację funkcjonalną; sama obecność wymagania
nie oznacza realizacji całego przepływu ani potwierdzonego wdrożenia.

## Rozszerzenia i wyłączenia

Rozszerzenia poza MVP, zgodnie z sekcją 3 `Funkcjonalności.txt`:

- przypisywanie uczestników do poszczególnych produktów w ramach jednego wydatku;
- przekierowanie do widoku przelewu bankowego z poziomu podziału wydatków;
- integracje z systemami e-paragonów;
- wdrożenie MCP.

Prognoza występuje w słowniku pojęć, ale nie stanowi przez to automatycznie
wymagania MVP.

Poza zakresem projektu są wykonywanie przelewów między użytkownikami,
przechowywanie środków, obsługa kart płatniczych, zastępowanie aplikacji bankowej
i doradztwo finansowe.

## Cele jakościowe

| Obszar | Cel projektowy |
| --- | --- |
| OCR | Co najmniej 90% poprawnie odczytanych pól liczbowych |
| OCR | Poprawna kwota do zapłaty i data zakupu dla co najmniej 95% paragonów |
| Kategoryzacja | Macro F1 co najmniej 0,8 dla zdefiniowanych kategorii |

Są to wymagania, a nie potwierdzone wyniki. Dokumentacja nie dostarcza tu
protokołu ewaluacji ani zbioru referencyjnego; należy je ustalić przy pracach
nad oceną modeli.

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
