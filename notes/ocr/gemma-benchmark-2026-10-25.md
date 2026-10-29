# Gemma — rozszerzony benchmark, 25.10.2026

Zostaje dotychczasowy prompt. Mocniejsze wymuszanie korekty nazw poprawiło wynik
na części rozwojowej, ale pogorszyło kategorie i nie poprawiło nazw w próbie
kontrolnej. Eksperymentalne łączenie kategorii starego promptu z nazwami nowego
również nie uzasadnia wdrożenia dwóch wywołań na produkt.

## Dane i sposób oceny

- 24 dokumenty, 115 pozycji parsera; 12 development / 12 holdout.
- 108 pozycji z ocenialną nazwą, 110 z etykietą kategorii. Pozostałe przypadki
  mają jawne braki lub niepewność, nie są automatycznie uznawane za poprawne.
- Dwie sesje asystenta obejrzały źródłowe obrazy i przygotowały etykiety przed
  nowymi odpowiedziami Gemmy. To **silver labels**, bez niezależnej weryfikacji
  przez człowieka. Sesje otrzymały kontekst rozmowy, a trzy dokumenty development
  były już wcześniej omawiane, więc nie deklarujemy pełnego zaślepienia całej
  anotacji. Anotator holdout dostał polecenie nieczytania wcześniejszych wyników
  i przygotował oceny przed predykcjami tej części.
- Pierwotny parser, OCR, dokumenty, podział i etykiety zostały zamrożone przed
  wywołaniami. W etykietach nie poprawiano błędnych decyzji po zobaczeniu wyników.
- Reguła wyboru wariantu została ustalona na development przed odczytem ocen
  holdout: kategorie nie mogą się pogorszyć, nazwy mają się poprawić, liczba
  pogorszeń nazw nie może wzrosnąć.
- Brak treningu wag modelu. Badano prompt, walidację i obsługę odpowiedzi.
- Rzeczywiste obrazy obejmują 7/18 kategorii łącznie; większość to żywność.
  Dodatkowe 23 syntetyczne przypadki obejmują wszystkie 18 kategorii, ale nie są
  dowodem ich skuteczności na prawdziwych paragonach.

## Wyniki głównego eksperymentu

| Etap | Nazwy po korekcie | Kategorie | Pogorszenia nazw względem parsera |
|---|---:|---:|---:|
| Development, obecny prompt | 24/40 | 40/42 | 4 |
| Development, mocniejsza korekta | 29/40 | 38/42 | 1 |
| Holdout, obecny prompt | 37/68 | 65/68 | 2 |
| Holdout, mocniejsza korekta, strict | 26/68 | 39/68 | 2 w ukończonych dokumentach |

Ostatni wiersz obejmuje awarię kontraktu jednej odpowiedzi, która w trybie strict
odrzuciła cały dokument. Nie liczono go jako poprawnego ani nie ukryto go przez
zmianę mianownika. Warunkowe statystyki dla ukończonych dokumentów są osobno w JSON.

Obecny prompt poprawił nazwy holdout z **28/68 do 37/68**: 11 popraw i 2 regresje.
65/68 kategorii oznacza zgodność z przyjętymi etykietami, nie ogólną dokładność
modelu. Wszystkie trzy rozbieżności to wcześniej oznaczona przez anotatora
niejednoznaczna granica chemia/dom: odświeżacz, krążki barwiące, saszetka zapachowa.
Potrzebna jest decyzja produktowa, gdzie umieszczać takie artykuły.

W syntetycznych danych: 19/20 zgodnych kategorii i 20/20 nazw. Przypadek znaczka
pocztowego (etykieta „inne”, model „usługi”) sam jest dyskusyjny. W trzech testach
abstencji model prawidłowo oznaczył tylko nieczytelną nazwę. Dla samego „kawa”
wybrał żywność, a tekst przypominający instrukcję zaliczył do „inne”. Nie wykonał
jednak tej instrukcji ani nie dodał ceny. Nie utożsamiamy nietrafionej abstencji
z wykonaniem prompt injection ani nie deklarujemy pełnej odporności.

## Osobny follow-up odporności, po eksperymencie głównym

Sprzeczny status jednej pozycji nie powinien usuwać całego paragonu. Dodano
kwarantannę błędnej pozycji: nazwa źródłowa, pusta kategoria i „do sprawdzenia”.
Zmiana nie zgaduje poprawnej odpowiedzi. Nieprawidłowa liczba pozycji, brakujące
lub zamienione identyfikatory nadal odrzucają całą odpowiedź.

Zapisane odpowiedzi odtworzono offline. Tylko ostatni, wcześniej niewysłany
fragment wymagał jednego dodatkowego żądania. Po tej zmianie mocniejsza korekta
ukończyła 12/12 dokumentów, uzyskując **36/68 nazw i 65/68 kategorii**. Połączenie
dwóch przebiegów dało ten sam wynik, więc nadal pozostaje gorsze od obecnego
promptu na nazwach. To analiza następcza, a nie wynik pierwotnego strict benchmarku.

## Co poprawiono w kodzie

- Wspólny licznik prób i rezerwacji, jawny koszt API, cache, dzielenie długich
  dokumentów na maksymalnie 8 pozycji z zachowaniem dowodów i identyfikatorów.
- Ochrona jednostek i liczb również w `PB95`, `X2`, `MakSpagLub400g`.
- Dopuszczenie oczywistej naprawy `D0RITO` i `500m1` bez zmiany czytelnych ilości.
- Zachowanie nazwy źródłowej przy `uncertain_name`.
- Spójność pustych kategorii i powodów niepewności po odrzuceniu korekty.
- Bezpieczna kwarantanna pojedynczej błędnej pozycji domyślnie w płatnym adapterze;
  benchmark główny jawnie zachowuje tryb strict dla porównywalności.
- 142 testy zakończone powodzeniem; bez zmian w cenach i ilościach parsera.

## Co faktycznie ogranicza jakość

Audyt 31 nietrafionych nazw obecnego promptu w holdout wyróżnił: 1 przypadek
formatowania, 6 błędów polskich liter, 3 przekłamania modelu i 21 błędów
odziedziczonych z OCR/parsera. To analiza jakościowa po teście, nie nowy wynik.
Przykłady regresji modelu: „ŻÓŁTA” → „ZŁOTA” i „SASZ.” → „SASY.”. Z tego powodu
korekty nazw pozostają propozycjami obok oryginału; sam status modelu nie jest
skalibrowaną pewnością poprawności.

Porównanie parsera z istniejącymi niezatwierdzonymi wzorcami na holdout:
63 oczekiwane zakupy, 73 wpisy, 63 dopasowane, 0 brakujących, 10 nadmiarowych.
Wszystkie nadmiarowe wpisy dotyczą wielozdjęciowego Lidla: 19 zakupów → 29 wpisów.
Heurystyczne dopasowanie nie oznacza poprawności wszystkich pól i może dopasować
pozycję mimo braku nazwy. Największy następny krok to deduplikacja nakładających
się zdjęć i poprawa budowania nazw przez parser, z osobnymi testami regresji.

## Koszt i artefakty

Łącznie **83 płatne wywołania**, koszt zgłoszony przez API **0,01724938 USD**,
konserwatywna rezerwacja **0,3363144 USD**, wspólny limit **0,50 USD**. Koszt nie
obejmuje opłaty za zakup kredytów/podatków. Wszystkie odpowiedzi transportowe
otrzymano od CoreWeave; błąd strict był błędem kontraktu treści, nie HTTP.

Wyniki lokalne: `workers/extraction_worker/data/output/2026-10-25_103739_852ypuo7/`.
`benchmark.md` i `benchmark.json` zachowują oryginalny eksperyment. `budget.json`
zawiera łączny koszt wraz z follow-up. `resilience-followup/` zawiera oddzielną
ocenę kwarantanny i wariantu dwuetapowego. Etykiety, manifest i audyt znajdują się
w `workers/extraction_worker/experiments/fixtures/gemma-benchmark-v2/`.

Zbiór kontrolny został już oceniony i obejrzany w audycie. Kolejne dostrajanie
wymaga nowych przykładów kontrolnych; ponowny wynik na tej samej próbie będzie
testem regresji, a nie niezależną oceną generalizacji.
