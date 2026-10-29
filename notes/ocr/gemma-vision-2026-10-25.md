# Gemma 4 31B bez OCR — eksperyment 2026-10-25

Gemma potrafi odczytać dokument ze zdjęć i dobrze scaliła nakładające się fragmenty,
ale ta próba nie uzasadnia zastąpienia całego pipeline'u. Mniej duplikatów i więcej
poprawnych nazw towarzyszyło błędom dat i cen jednostkowych. Walidator wykrył
wszystkie cztery błędne pola finansowe w najtrudniejszym dokumencie.

## Próba i metoda

Pięć dokumentów, sześć oryginalnych JPEG, 28 zakupów: screenshot aplikacji, ALDI,
Żabka z rabatem i kaucją, pizzeria z kolumnami netto/VAT/brutto oraz Lidl na dwóch
nakładających się zdjęciach. Brak lokalnego OCR, kadrowania, skalowania i obracania.
W jednym żądaniu wysłano oba fragmenty Lidla. Model otrzymał instrukcję ekstrakcji,
kontrakt kwot i 18 kategorii; nie otrzymał nazw plików, referencji, OCR ani parsera.

Referencje `workers/extraction_worker/experiments/fixtures/gemma-vision-v1/`
sprawdzono wizualnie przed predykcjami. To referencje asystenta, bez niezależnego
zatwierdzenia człowieka. Próba celowa, częściowo wcześniej analizowana, obejmująca
głównie żywność; nie estymuje jakości na dowolnych dokumentach ani wszystkich
kategoriach. Nie trenowano wag modelu.

Baseline to zachowany OCR → parser regułowy → tekstowa Gemma z benchmarku
`workers/extraction_worker/data/output/2026-10-25_103739_852ypuo7/`, wariant baseline.
Oceniono proponowane nazwy Gemmy i oryginalne kwoty parsera, bez ich poprawiania.
Baseline korzystał z CoreWeave, vision z DeepInfra przez OpenRouter: to różnica
usługodawcy, która ogranicza przypisywanie całego efektu samej modalności.

## Wyniki po lokalnym dopuszczeniu zbiorczego wskazania obrazu

| Miara | OCR → parser → Gemma | Sama Gemma ze zdjęć |
|---|---:|---:|
| Unikalne zakupy dopasowane do 28 referencyjnych | 28/28 | 28/28 |
| Nadmiarowe pozycje | 10 | 0 |
| Ilości | 27/28 | 28/28 |
| Ceny jednostkowe | 28/28 | 25/28 |
| Kwoty przed rabatem | 28/28 | 28/28 |
| Kwoty produktów po rabacie | 26/28 | 27/28 |
| Kategorie dopasowanych zakupów | 27/28 | 27/28 |
| Suma towarów / kwota do zapłaty | 5/5 i 5/5 | 5/5 i 5/5 |
| Data | 5/5 | 3/5 |
| Kaucja | 1/1 | 1/1 |

Dopasowanie pozycji jest heurystyczne i zachowuje kolejność; nadmiar jest raportowany
osobno, więc zgodność kwot dopasowanych zakupów nie oznacza poprawnego dokumentu.
Lidl: baseline zwrócił 29 pozycji zamiast 19, vision dokładnie 19 bez duplikatów.
Screenshot: wszystkie 3 zakupy, ilości, ceny i sumy poprawne w obu wariantach.
Żabka: vision prawidłowo rozdzieliła 26,00 zł przed rabatem, −9,20 zł rabatu,
16,80 zł towarów, 2,00 zł kaucji i 18,80 zł do zapłaty. Pizzeria: poprawne brutto
56,10 + 11,90 + 75,00 = 143,00 zł, bez ponownego doliczania VAT.

Błędy vision:

- W Żabce i Lidlu rok 2020 zamiast widocznego 2026. Sama spójność sum tego nie wykryje.
- Lidl: Marlenka 21,99 zamiast 20,17 po rabacie, mimo odczytanego rabatu −1,82.
- Lidl: boczek 15,18 zamiast 7,59 za sztukę; bułki 8,34 zamiast 1,39;
  dynia 6,39 zamiast 3,99 za kg. Kwoty przed rabatem skopiowane do cen jednostkowych.
- Lody z pizzerii sklasyfikowane jako żywność zamiast restauracje i kawiarnie.
- Literówki m.in. `Brytwa mna` zamiast `Brytfanna`, `kutki` zamiast `kulki`,
  `Butka` zamiast `Bułka`; tylko brytfannę model oznaczył jako niepewną.

Walidator zwrócił dla Lidla `item_discount_mismatch`, trzy `item_price_mismatch`
i `total_mismatch`. Wartości pozostały bez automatycznej korekty.

Dosłowne nazwy po normalizacji: baseline 12/28, vision 20/28. Nie interpretować
tej różnicy jako ośmiu nowych poprawnie rozpoznanych produktów: część referencji
zawiera znaczniki VAT, a tekstowa Gemma je usuwa. Warianty `Żabka` vs pełna nazwa
sklepu również obniżają dosłowną metrykę sklepu bez błędu semantycznego. Brak
wydrukowanego rabatu vision pozostawiała jako null (8 pozycji), wzorzec ma 0,00;
to luka normalizacji, a nie osiem błędnie naliczonych rabatów.

## Koszt, czas i ślad eksperymentu

Dokładnie **5 płatnych wywołań**, bez ponowień, łącznie **0,003994155 USD**.
Historyczne tekstowe wzbogacanie tych samych pięciu dokumentów: **0,00182924 USD**.
Vision kosztowała około **2,18×** tyle w tej próbie; zakres zadań i cache są różne,
a koszt baseline'u nie obejmuje lokalnego OCR. Brak kosztu trenowania.
API vision zajęło kolejno 36,678; 22,526; 18,918; 29,838 i 93,957 s. Tych czasów
nie porównujemy z samym historycznym wzbogacaniem jako czasem pełnego pipeline'u.

Konserwatywna rezerwacja pełnego kontekstu wyniosła 0,417792 USD przy limicie
0,45 USD. Rzeczywisty koszt osobno pochodzi z usage.cost każdej odpowiedzi.

Pierwotne płatne uruchomienie:
`workers/extraction_worker/data/output/2026-10-25_110537_36nfgfgi/`.
Cztery odpowiedzi przeszły dekoder, piąta miała kompletną treść, lecz dekoder
odrzucił `image_evidence.field="items"` zamiast ścieżki pojedynczej pozycji.
Nie był to błąd transportu ani brak wyników ekstrakcji. Rozszerzono lokalny dekoder
o zbiorcze wskazania `items`, `deposits`, `deposit_returns`. Nie zmieniono promptu,
predykcji, wartości ani etykiet referencyjnych. Pierwotny raport 4/5 zachowano.

Replay pięciu tych samych odpowiedzi z cache bez dostępu do sieci i bez kosztu:
`workers/extraction_worker/data/output/2026-10-25_110950_1sqqmeu8/`.
Jego `comparison.json`/`comparison.md` zawierają pełne porównanie 5/5; ledger replayu
ma koszt 0, osobne `vision_responses_cost_usd` pokazuje koszt oryginalnych odpowiedzi.
Czasy replayu to czas lokalnego dekodowania, nie czas API.

## Rekomendacja

Zachować vision jako eksperymentalną alternatywę dla wielu zdjęć i trudnego układu.
Następne porównanie powinno sprawdzić podejście hybrydowe: vision wskazuje zakupy
i ich scalanie, a parser/walidacja kontrolują liczby i daty. Nie mieszać wyników
automatycznie bez pewnego przypisania pozycji. Zanim włączymy to domyślnie, zamrozić
reguły normalizacji nazw/rabatów i sprawdzić nowe dokumenty, których nie użyto
do przygotowania promptu. Obecny eksperyment nie zmienia ścieżki aplikacji.
