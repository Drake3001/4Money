# Gemma: trudne naturalne paragony, 25.10.2026

Na długich, gęstych paragonach wyniki są wyraźnie gorsze niż we wcześniejszej próbie. Zdjęcie + OCR pomaga odzyskać pozycje i uniknąć duplikatów, ale nie zapewnia poprawnego przypisania cen ani tożsamości produktów. Obecny wynik wymaga kontroli użytkownika; poprawne kategorie i suma dokumentu nie wystarczają do akceptacji.

## Próba i metoda

Zamrożone przed wywołaniami referencje obejmują **7 nowych fizycznych transakcji i 140 unikalnych zakupów**. Pięć paragonów ma odpowiednio 31, 30, 29, 23 i 20 pozycji; występują liczne rabaty, drobny/blady druk, zagięcia i ilości ułamkowe. Dwa krótkie przypadki sprawdzają powtarzające się sery ważone oraz obcy paragon widoczny w tle. Paragon 30-pozycyjny w próbie głównej ma dwa zachodzące na siebie oryginalne zdjęcia.

Oddzielne kontrole to pojedyncze pełne zdjęcie tej samej transakcji oraz naturalnie urwane pierwsze zdjęcie z 19 pełnymi i jedną częściową pozycją. Łącznie 9 prezentacji; dodatkowe ujęcia nie zwiększają liczby unikalnych transakcji ani zakupów. Nie testowano tu paragonów 50–100-pozycyjnych, nowych screenshotów ani całkowicie nieczytelnych zdjęć.

Porównano OCR → parser → Gemma z oryginalnym zdjęciem + surowym OCR (z geometrią). Model google/gemma-4-31b-it; bez zmiany promptów po wynikach, bez ponowień, bez referencji i nazw plików w wejściu modelu. OCR pochodzi z zapisanego wcześniejszego przebiegu. Referencje sprawdzili asystenci na obrazach przed predykcjami; nie zatwierdził ich niezależnie człowiek. Próba celowa, przeważnie spożywcza, nie reprezentuje wszystkich kategorii.

## Wyniki główne

| Miara | OCR → parser → Gemma | Zdjęcie + OCR |
|---|---:|---:|
| Dokumenty zaakceptowane przez kontrakt | 7/7 | 6/7 |
| Brakujące pozycje | 5 | 31* |
| Nadmiarowe pozycje | 19 | 0 |
| Zgodne daty | 7/7 | 4/7 |
| Zgodne sumy dokumentu | 7/7 | 6/7 |
| Zgodne końcowe kwoty dopasowanych zakupów | 133/140 | 94/140 |
| Błędne, niepuste końcowe kwoty zakupów | 0 | 15 |
| Brakujące końcowe kwoty zakupów | 7 | 31* |
| Kategorie zgodne z referencją | 119/140 | 109/140 |

\* Jeden dokument z 31 pozycjami został w całości odrzucony przez sprzeczne metadane `unreadable_name`, mimo zwróconych nazw i kategorii. To awaria protokołu; nie oznacza, że model nie zobaczył 31 produktów. Surowa odpowiedź zachowuje 31 wierszy, ale ma również braki kwot/ilości i błędne nazwy. Nie naprawiano jej po fakcie w głównym wyniku.

Kwoty ocenia się po heurystycznym dopasowaniu pozycji, a nadmiar raportuje osobno: wynik 133/140 ścieżki tekstowej nie oznacza poprawnego całego paragonu przy 19 duplikatach. Brak niewydrukowanego zerowego rabatu nie jest mylony z błędnym niezerowym rabatem. Literalna zgodność nazw 48/140 i 68/140 nie stanowi pełnej oceny semantycznej: karze też poprawne rozwinięcia skrótów.

## Co faktycznie zawiodło

- **Dwa zdjęcia, 30 zakupów:** parser daje 49 pozycji, czyli 19 nadmiarowych. Gemma daje poprawne 30 nazw/kategorii bez duplikatów, ale na 12 kolejnych wierszach przypisuje kwoty sąsiednim produktom. Audyt zdjęć potwierdza rzeczywisty błąd modelu, nie mechanizmu dopasowania. Ostrzeżenie sumy wykrywa problem. Pojedyncze pełne ujęcie tej samej transakcji również zawodzi na rabatach, lecz nie ma tej serii przesunięć.
- **29 zakupów:** hybryda odczytuje poprawnie wszystkie znane dane finansowe i kategorie. Jednocześnie zmienia masło 200 na „Mascarpone 250g”, a napój owsiany na „Bananugustowe 1l”. Wszystkie pozycje mają `needs_review=false`. Prawidłowe liczby i szeroka kategoria „żywność” nie wykrywają wymyślonej tożsamości produktu.
- **23 zakupy:** hybryda myli sklep, odczytuje rok 2020 zamiast 2026 i rabat ogórka −0,80 zamiast −0,62; końcowa kwota 8,10 zamiast 8,28. Suma pozycji nie zgadza się z dokumentem.
- **20 zakupów:** parser pomija trzy produkty; hybryda odzyskuje wszystkie 20 i ich znane wartości finansowe/kategorie. Nadal nie podaje sklepu. Zmienia jednak sałatę masłową na „musztardową”, przekręca skrót migdałów i dopisuje kg w pięciu miejscach bez wydrukowanej jednostki. Jednostki te nie są oceniane przez powyższą metrykę liczb.
- **31 zakupów:** parser pomija dwa wiersze i kwotę szynki. Walidator arytmetyczny nie zgłasza rozbieżności sumy, ponieważ przy brakujących kwotach nie wykonuje pełnej kontroli; parser ma jednak 11 ostrzeżeń. Nie jest to całkowicie bezgłośna awaria. Hybryda dodatkowo odpada przez błędny powód niepewności i wymyśla niektóre nazwy/rozmiary.
- **Krótkie kontrole:** hybryda prawidłowo zachowuje dwa osobne sery o różnych masach, poprawia jednostkę koncentratu i ignoruje obcy rachunek w tle. Wyniki krótkich przypadków nie opisują trudności długich paragonów.

## Urwany dokument

Do obu metod trafiło wyłącznie pierwsze oryginalne zdjęcie i jego strona OCR; żadnego tekstu ani obrazu dolnej części. Parser pozostawia datę, walutę, sumę i kwotę do zapłaty puste; pomija częściowo widoczną dwudziestą pozycję. Surowa hybryda zwraca 19 pełnych poprawnych wierszy i nazwę marchewki z pustymi polami liczbowymi, nie zgaduje daty ani pełnej sumy. Dopisuje jednak niewidoczną walutę PLN. Cała odpowiedź odpada przez `unreadable_name` przy niepustej nazwie i kategorii marchewki.

Kontrakt nie rozróżnia nieznanych kaucji od pustej listy kaucji. Czas, forma płatności i podsumowanie podatków również nie są obsługiwanymi polami; kontrola nie przyznaje za nie punktów za abstencję. Nie przyznaje ich także za brak dopasowania urwanej pozycji.

## Koszt, powtarzalność i artefakty

37 nowych wywołań: **0,035280360 USD**, w tym tekst 0,007573560 USD, zdjęcie + OCR 0,027706800 USD. Limit eksperymentu 0,35 USD; to koszt tego badania, bez historycznego OCR i wcześniejszych eksperymentów. Dwa tekstowe fragmenty były już w cache. Każda nowa próba ma rezerwację i zapis rozliczenia.

- Przebieg płatny: `workers/extraction_worker/data/output/2026-10-25_164536_e3h8qs05/`.
- Odtworzenie offline: `workers/extraction_worker/data/output/2026-10-25_170604_ilptkz2k/`: **0 wywołań, 0 USD**, identyczne podsumowanie, kohorty oraz hashe referencji, żądań i parsera.
- Wyniki i surowe odpowiedzi: `comparison.json`, katalogi poszczególnych przypadków; audyty wizualne: `audit-development.md`, `audit-holdout.md`.
- `posthoc-audit-corrected.json` koryguje wyłącznie sumowanie nowych kosztów tekstowych fragmentów w pomocniczym audycie. Oryginalny `posthoc-audit.json` zachowano; główny ledger, porównanie i metryki nie wymagały korekty.
- Weryfikacja odtworzenia: `offline-verification.json`. Pełny zestaw przed ostatnią korektą raportowania: 316 testów zaliczonych. Po korekcie: 12 testów audytu/hard-study zaliczonych (początkowy problem z uprawnieniami katalogu tymczasowego rozwiązano przez katalog w workspace).

## Następny etap implementacji

Najpierw powiązanie całego bloku liczbowego z pozycją i deduplikacja nakładających się zdjęć. Następnie jawny stan „nie można sprawdzić sumy” dla brakujących kwot oraz kierowanie takich dokumentów do kontroli. Osobno zabezpieczenie tożsamości/numerów produktu i spójności metadanych niepewności. Błąd jednej adnotacji nie powinien usuwać poprawnego odczytu całego dokumentu; wymaga to jawnej polityki zachowania danych i oznaczenia błędu, nie cichego poprawiania historycznych odpowiedzi.

Każda kolejna zmiana wymaga ponownego odtworzenia tej zamrożonej próby oraz osobnego, nieużytego wcześniej zbioru. Nie ma jeszcze podstaw do uznania którejkolwiek ścieżki za niezawodną, samodzielną obsługę trudnych paragonów.
