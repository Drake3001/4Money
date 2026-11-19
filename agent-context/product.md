# Produkt i wymagania 4Money

Ten dokument opisuje cel i zakres produktu, a nie stopień ukończenia kodu.
Bieżące możliwości i braki są w [statusie](implementation-status.md), reguły
modelu w [domenie](domain.md), szczegółowe kontrakty w dokumentacji modułów.
Wymagania źródłowe są w lokalnym `../doc/` poza repozytorium. Ich oznaczenia MVP
poniżej zachowują odrębność od późniejszych ustaleń implementacyjnych.

## Cel i odbiorcy

4Money (For Money, 4M) ogranicza ręczną pracę przy rejestrowaniu, klasyfikacji
i analizie wydatków osobistych oraz współdzielonych. Odbiorcami są osoby
kontrolujące budżet, pary, rodziny, współlokatorzy i grupy wyjazdowe.

Wspólny model łączy zakupy wpisane ręcznie, odczytane z paragonów i oparte na
transakcjach bankowych. Wyróżnikiem jest analiza produktów, nie tylko całych
płatności: zakup w jednym sklepie może obejmować żywność, kosmetyki i chemię.
Kategorie pozycji pozwalają analizować te koszty niezależnie od sprzedawcy.
Powiązanie źródeł nie oznacza utworzenia kilku wydatków dla jednego zakupu.

System pokazuje udziały użytkowników i wzajemne zobowiązania. Przekazanie pieniędzy
następuje poza aplikacją; rozliczenie w systemie jest zapisem zdarzenia, które
już się odbyło. Sugestia rozliczenia nie jest zleceniem płatniczym.

## Reprezentatywny scenariusz

1. Użytkownik płaci za zakupy; transakcja trafia do systemu przez import bankowy.
2. Przesyła paragon; OCR proponuje sprzedawcę, datę, kwotę, produkty i kategorie.
3. Sprawdza wynik, poprawia błędy i zatwierdza formularz wydatku.
4. Przypisuje płatnika i uczestników całego wydatku oraz proporcje podziału.
5. System aktualizuje zobowiązania, wykorzystanie budżetów i dane do analiz.

Docelowo system może również proponować połączenie paragonu z transakcją;
użytkownik akceptuje lub odrzuca propozycję. Automatyczne dopasowanie jest
rozszerzeniem poza MVP. Ten scenariusz nie dowodzi działania wszystkich etapów
end-to-end ani spełnienia celów jakościowych przez aktualną integrację.

## Zakres funkcjonalny i MVP

Tylko podsekcje oznaczone `(MVP)` w `Funkcjonalności.txt` stanowią jawny zakres
MVP. Obecność modelu, endpointu lub ekranu nie zmienia tej klasyfikacji.

| Obszar | Wymagana możliwość | Oznaczenie źródłowe |
| --- | --- | --- |
| Konto | Rejestracja, logowanie, wylogowanie, edycja danych i danych uwierzytelniających | 2.1, MVP |
| Wydatki | Ręczny zapis, historia, edycja, kategorie, wspólny model źródeł | 2.2, MVP |
| Paragony | Zdjęcie w aplikacji mobilnej, upload obrazu/zrzutu, OCR i korekta | 2.3, MVP |
| Kategoryzacja | Propozycje dla wydatków i produktów, możliwe do zmiany | 2.4, MVP |
| Bankowość | Import transakcji przez dostawcę, sandbox lub symulator | 2.5, MVP |
| Limity | Definicje budżetów, kategorie, alerty przed i po przekroczeniu | 2.6, bez oznaczenia MVP |
| Grupy | Tworzenie, zaproszenia, wiele członkostw, wydatki i powiadomienie o ich dodaniu | 2.7, MVP |
| Podział kosztów | Uczestnicy całego wydatku, udziały, prezentacja zobowiązań | 2.8, MVP |
| Raporty | Czas, kategorie, budżety, struktura produktów, widok osobisty i grupowy | 2.9, bez oznaczenia MVP |
| Powiadomienia | Wewnętrzna skrzynka użytkownika | 2.10, MVP |

Limity i raportowanie pozostają częścią projektu mimo braku oznaczenia MVP.
Podział całego wydatku należy do MVP, a przypisywanie uczestników do pojedynczych
produktów jest rozszerzeniem. Odczyt i kategoryzacja produktów nie wymagają
przez to osobnych rozliczeń każdej pozycji. Szczegóły formularza opisuje
[Expense API](../notes/EXPENSE_API.md).

## Uzgodnione zachowania produktu

Wynik OCR i kategoria są propozycjami możliwymi do ręcznej korekty. Nie zapisuj
automatycznie wyniku ekstrakcji jako zaakceptowanego wydatku. Użytkownik musi
mieć możliwość weryfikacji, także gdy usługa zwróci HTTP 200. Nieznana kwota,
data lub nazwa nie uprawnia do dopowiadania danych finansowych.

Konta wymagają e-maila. Obsługa obejmuje potwierdzenie adresu, sesje, zmianę
e-maila, reset hasła i uzgodnioną dezaktywację. Dezaktywacja nie jest fizycznym
usunięciem danych ani reaktywacją. Adres nieaktywnego konta może zostać ponownie
użyty, login pozostaje zajęty. Google nie łączy automatycznie kont lokalnych po
zgodnym adresie. Kontrakt konta ma główne miejsce w [User](../backend/ZPI4M.Backend/src/modules/ZPI4M.User/README.md).

Powiadomienia w bieżącym zakresie to inbox web/mobile i indywidualny stan odczytu.
Powiadomienie o dołączeniu nie jest zaproszeniem push. Nie dodawaj teraz tokenów
urządzeń, integracji dostawcy push ani kolejki wysyłki. Źródłowe wymagania alertów
budżetu nie określają progów i częstotliwości; aktualnie uzgodnione reguły
opisuje [Limits](../backend/ZPI4M.Backend/src/modules/ZPI4M.Limits/README.md).

Budżet PLN może być kalendarzowy według Warsaw albo jednorazowy Custom ze
skończonym zakresem dat, np. podróż 25 marca–10 kwietnia. Zakres podróży nie
resetuje się na granicy miesiąca. Daty pozostają stałe, a po zakończeniu cel
nie jest edytowalny. Custom działa niezależnie od innych limitów: miesięczny
limit i 3-dniowy wyjazd mogą współistnieć, podobnie jak nakładające się wyjazdy.
Każdy liczy pasujące wydatki i wysyła własne alerty. Zapis jednego nie wyłącza
pozostałych. Szczegóły zakresów i konfliktów kalendarzowych należą do kontraktu
Limits; nie wyprowadzaj reguł z dawnych modeli okresów.

## Rozszerzenia i wyłączenia

Rozszerzenia poza MVP obejmują automatyczne dopasowanie banku do paragonu,
push, interaktywny push z pytaniem o dodanie transakcji jako wydatku, podział
produktów między uczestników, przejście do widoku przelewu bankowego, integracje
e-paragonów i MCP. Nie są automatycznie zakresem zadania dotyczącego istniejących
wydatków lub inboxa. Źródła nie ustanawiają obecnie prognozowania przyszłych
wydatków jako funkcji; trendy historyczne i alerty nie oznaczają takiego wymagania.

Poza projektem są wykonywanie przelewów, przechowywanie środków, obsługa kart,
zastępowanie aplikacji bankowej i doradztwo finansowe. Sam import danych nie
nadaje aplikacji uprawnień do operacji na rachunku. Dostępny sandbox lub symulator
jest dopuszczalny dla MVP; nie nazywaj go potwierdzoną integracją produkcyjną.

## Cele jakościowe

| Obszar | Wymagany cel |
| --- | --- |
| OCR | Co najmniej 90% poprawnie odczytanych pól liczbowych |
| OCR | Poprawna kwota do zapłaty i data dla co najmniej 95% paragonów |
| Kategoryzacja | Macro F1 co najmniej 0,8 dla zdefiniowanych kategorii |

To cele projektowe, nie wyniki aktualnego pipeline. Pomiar musi wskazać model,
wersję pipeline, zbiór, metrykę i protokół. Poprawienie pojedynczego paragonu
przez użytkownika ani historyczny eksperyment innym modelem nie dowodzą realizacji
celu. Narzędzia: [ewaluacja workera](../workers/extraction_worker/experiments/README.md).

Roadmapa wskazuje kolejność: analiza, rdzeń, paragony, Open Banking, UI, raporty,
integracja i finalizacja. Nie jest harmonogramem ani potwierdzeniem ukończenia.
Dla zadania wymagającego nowej decyzji produktu zapisz uzgodnienie w odpowiednim
kontrakcie zamiast traktować brak szczegółu w źródłach jako dowolność implementacji.

## Źródła

- [Wizja](../../doc/Wizja%20projektu.txt), [funkcjonalności](../../doc/Funkcjonalności.txt),
  [cele jakościowe](../../doc/Fiszka%20projektowa.txt).
- [Słownik](../../doc/Słownik%20pojęć.txt), [roadmapa](../../doc/4Money-Roadmap.jpg).

Dokumenty w `../../doc/` są zewnętrznymi materiałami lokalnego projektu. Przy
rozbieżności ze źródłem nazwij różnicę i zakres ustalenia; kod potwierdza zachowanie,
ale nie zastępuje dokumentu wymagań.
