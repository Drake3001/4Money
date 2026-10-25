# Kategorie i korekta produktów — wersja 1

Uzgodniony słownik dla parsera wspomaganego Gemmą. Źródło definicji w kodzie: `workers/extraction_worker/extraction/categories.py`. Klasyfikujemy pozycje osobno, nie cały paragon.

| Kategoria | Zakres |
|---|---|
| Żywność i napoje | Zakupy spożywcze i napoje bezalkoholowe, również gotowa żywność ze sklepu. |
| Restauracje i kawiarnie | Posiłki i napoje bezalkoholowe z gastronomii, także dostawy; wymaga kontekstu gastronomicznego. |
| Alkohol i tytoń | Napoje alkoholowe, papierosy i produkty nikotynowe, również kupione w restauracji. |
| Ubrania i dodatki | Odzież, obuwie, torby, biżuteria i dodatki odzieżowe. |
| Elektronika | Sprzęt elektroniczny, przewody, ładowarki i części elektroniczne. |
| Dom i ogród | Wyposażenie, meble, narzędzia, rośliny, artykuły kuchenne i ręczniki papierowe. |
| Chemia gospodarcza | Detergenty i środki czystości, np. płyn do naczyń, proszek do prania, płyn do WC. |
| Kosmetyki i higiena | Pielęgnacja i higiena osobista, np. szampon, pasta do zębów, podpaski, papier toaletowy. |
| Zdrowie | Leki, wyroby medyczne, badania, wizyty lekarskie, dentysta i rehabilitacja. |
| Zwierzęta | Karma, akcesoria dla zwierząt, ich pielęgnacja i weterynarz. |
| Transport i samochód | Paliwo, części, serwis auta, parking, taxi, komunikacja miejska i przejazdy lądowe. |
| Podróże i noclegi | Hotele, loty, wycieczki i usługi turystyczne; nie zgaduj celu zwykłego przejazdu. |
| Rozrywka i wydarzenia | Kino, koncerty, gry, abonamenty rozrywkowe i książki rekreacyjne. |
| Sport i hobby | Siłownia, sprzęt sportowy, zajęcia rekreacyjne i materiały hobbystyczne. |
| Edukacja | Kursy, podręczniki, szkolenia i opłaty za naukę. |
| Rachunki i opłaty | Media, prąd, gaz, woda, telefon, internet i opłaty administracyjne. |
| Usługi | Fryzjer, barber, krawiec, naprawa obuwia, dorabianie kluczy; tylko usługi bez trafniejszej kategorii. |
| Inne | Rozpoznany wydatek, dla którego żadna z pozostałych kategorii nie pasuje; nie służy do oznaczania niepewności. |

## Zasady pierwszeństwa

Klasyfikuj każdą pozycję osobno. Konkretny cel ma pierwszeństwo
przed usługami: weterynarz -> zwierzęta, dentysta -> zdrowie, mechanik -> transport
i samochód. Nazwa sklepu sama nie wyznacza kategorii: kanapka ze stacji to żywność
i napoje, paliwo to transport i samochód. Piwo bezalkoholowe 0,0% to żywność i
napoje, zwykłe piwo to alkohol i tytoń. Kawa w paczce to żywność i napoje;
kawa podana w kawiarni to restauracje i kawiarnie. Ręcznik papierowy to dom i
ogród; papier toaletowy to kosmetyki i higiena; płyn do WC to chemia gospodarcza.
Nie wnioskuj celu prezentowego, inwestycyjnego ani podróży z samego miejsca
zakupu. Prezenty, inwestycje i nieustalone nie są kategoriami. Gdy brak kontekstu
uniemożliwia wybór, zwróć category=null, needs_review=true i
review_reason=ambiguous_category. Dla nieczytelnej nazwy zwróć name_corrected=null,
category=null, needs_review=true i review_reason=unreadable_name.

## Korekta nazwy i niepewność

Zachowujemy `name_raw`, osobno zapisujemy `name_corrected`. Naprawiamy oczywiste literówki i polskie znaki. Skróty rozwijamy tylko jednoznacznie; nie dopowiadamy marki, smaku, gramatury ani innych cech. Można usunąć osobny końcowy znacznik VAT, np. `-A`. Nie zmieniamy liczby produktów, cen, ilości, rabatów ani dowodów OCR.

`needs_review=false` wymaga nazwy, kategorii i `review_reason=null`. Niepewność nazwy: zachowujemy opis i oznaczamy `uncertain_name`. Niepewna kategoria: `category=null`, `ambiguous_category`. Nieczytelna nazwa: obie wartości null i `unreadable_name`. Ochrona lokalna przy zmianie czytelnej liczby przywraca nazwę źródłową i oznacza `unsupported_name_change`. Status do sprawdzenia nie jest kategorią. Brak nazwy źródłowej nie uprawnia modelu do jej wymyślenia.

Same liczby nie wystarczają do wykrycia każdej błędnej zmiany znaczenia. Zgodność schematu i sum również nie dowodzi poprawności klasyfikacji. Nie traktujemy samooceny modelu jako skalibrowanego prawdopodobieństwa.

## Ewaluacja

Porównujemy nazwy przed i po, poprawy i pogorszenia, trafność kategorii oraz liczbę abstencji. Dokładność kategorii obejmuje także null jako brak poprawnej odpowiedzi; wynik dla automatycznie zaakceptowanych pozycji raportujemy osobno z liczbą ocenionych pozycji. Nazwy akceptowalne mogą mieć kilka wariantów. Ocena musi być związana z konkretnymi nazwami wejściowymi i wersją taksonomii.

Fixture `experiments/fixtures/gemma-small-review-v1.json` obejmuje sześć produktów z trzech obrazów obejrzanych przez asystenta. Nie jest niezależnie zatwierdzonym wzorcem człowieka ani reprezentatywnym benchmarkiem. W ocenie starego promptu kategorie uzyskały 5/6, nazwy przed 3/6 i po 4/6, poprawy 1, pogorszenia 0. Historyczne nazwy kategorii są jawnie mapowane; błędna decyzja o ręczniku pozostaje błędna. Nowy prompt wymaga osobnego, ograniczonego kosztowo testu.
