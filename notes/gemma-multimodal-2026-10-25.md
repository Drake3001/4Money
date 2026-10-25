# Zdjęcie kontra zdjęcie + OCR: 24 dokumenty, 105 zakupów

Najlepszy wynik całościowy w tej próbie dało **zdjęcie + pomocniczy OCR**:
wszystkie daty, ilości, ceny jednostkowe i wszystkie zakupy bez nadmiarowych wpisów.
Nie jest to jeszcze bezbłędny odczyt: pozostały trzy błędne kwoty po rabacie,
trzy rzeczywiste błędy kategorii i dopisane szczegóły kaucji. Sama Gemma ze zdjęcia
w większej próbie wypadła słabiej, niż sugerował pierwszy test pięciu dokumentów.

## Przebieg i zamrożona próba

Próba celowa: 24 dokumenty, 25 oryginalnych JPEG, 105 unikalnych zakupów. 19
dokumentów nie było wcześniej ocenianych wariantem vision; pięć stanowiło kontrolę
z poprzedniego eksperymentu. Jedyny dokument na dwóch nakładających się zdjęciach
pochodzi właśnie z poprzedniej próby, więc nie jest nowym potwierdzeniem skuteczności
scalania wielu zdjęć. Zachowano wcześniejszy podział 12 development / 12 holdout;
żadnego promptu nie zmieniano na podstawie nowych odpowiedzi.

Referencje i kategorie sprawdzono na zdjęciach przed nowymi predykcjami, oddzielnie
dla obu części. Status `assistant_verified_not_human`: brak niezależnego zatwierdzenia
człowieka i pełnego zaślepienia wobec wcześniejszej pracy. Nie trzeba było poprawiać
wcześniejszych wartości referencyjnych. Zakupy obejmują 7/18 kategorii; 87/105
to żywność. Nie jest to pomiar dokładności na wszystkich typach dokumentów.

Trzy warianty:

1. Zachowane wyniki OCR → parser regułowy → tekstowa Gemma z wcześniejszego benchmarku.
2. Oryginalne obrazy → Gemma → istniejący walidator.
3. Te same obrazy + zapisany OCR z tekstem, pewnością i geometrią → Gemma → ten sam walidator.

Model: `google/gemma-4-31b-it` przez OpenRouter. Wszystkie 48 odpowiedzi obrazowych,
w tym pięć odczytanych z cache, pochodzi z DeepInfra. Historyczny tekstowy wariant
korzystał z CoreWeave, więc jest również różnica dostawcy i czasu pomiaru. Model
nie otrzymał parsera, referencji ani etykiet. Nie zmieniano bajtów zdjęć. Część
JPEG ma EXIF6; zapisany OCR używa współrzędnych po EXIF-transpose. Nie wiemy,
czy dostawca zawsze normalizuje orientację identycznie; metadane zapisano w audycie.

## Główne wyniki

Mianowniki obejmują także dokument odrzucony technicznie. Dopasowanie pozycji jest
heurystyczne; nadmiarowe wpisy raportowane są osobno.

| Miara | OCR → parser → Gemma | Zdjęcie | Zdjęcie + OCR |
|---|---:|---:|---:|
| Przyjęte odpowiedzi dokumentów | 24/24 | 23/24 | 24/24 |
| Poprawne daty | 20/24 | 16/24 | 24/24 |
| Ilości zakupów | 97/105 | 97/105 | 105/105 |
| Ceny jednostkowe | 105/105 | 96/105 | 105/105 |
| Kwoty pozycji po rabatach | 102/105 | 92/105 | 102/105 |
| Niezerowe rabaty pozycji | 32/35 | 31/35 | 33/35 |
| Suma towarów / kwota do zapłaty | 24/24, 24/24 | 23/24, 23/24 | 24/24, 24/24 |
| Kaucje / zwroty kaucji: kwoty | 10/10, 2/2 | 10/10, 2/2 | 10/10, 2/2 |
| Nadmiarowe pozycje | 10 | 4 | 0 |
| Brakujące / odrzucone pozycje | 0 | 4 | 0 |
| Nazwy zgodne z transkrypcją po normalizacji VAT | 61/105 | 54/105 | 82/105 |
| Podstawowe kategorie | 97/105 | 96/105 | 98/105 |
| Kategorie z wcześniej dopuszczonymi alternatywami | 100/105 | 100/105 | 102/105 |

Jedna odpowiedź samego vision (`IMG_6882`) zawierała dwa różne bloki JSON oraz
komentarz „Wait…”. Została odrzucona jako niejednoznaczna, bez ponowienia i bez
wybierania dogodnej wersji. Cztery pozycje brakujące w metryce to ten jeden
odrzucony dokument, a nie dowód, że model nie zobaczył produktów na zdjęciu.

Po ograniczeniu do **19 nowych dokumentów / 77 zakupów**:

| Miara | Pipeline | Zdjęcie | Zdjęcie + OCR |
|---|---:|---:|---:|
| Przyjęte dokumenty | 19/19 | 18/19 | 19/19 |
| Kategorie podstawowe | 70/77 | 69/77 | 71/77 |
| Braki / nadmiar | 0 / 0 | 4 / 4 | 0 / 0 |

Granice kategorii zapisano przed predykcjami: torba zakupowa (`dom i ogród` / `inne`)
oraz trzy produkty do odświeżania i czyszczenia (`chemia gospodarcza` / `dom i ogród`).
Wynik podstawowy i wynik z alternatywami pozostają oddzielne.

Nazwy oceniane są jako transkrypcje, po usunięciu jedynie znanego końcowego znacznika
VAT, normalizacji interpunkcji, odstępów i wielkości liter. To nie jest pełna miara
tożsamości produktu: poprawne rozwinięcie skrótu czy przywrócenie polskiego znaku
może przegrać z literalnie skróconym lub nieakcentowanym wzorcem.

## Co pomogło, a co nadal zawodzi

OCR naprawił wszystkie siedem błędnych lat 2020 zamiast 2026 w przyjętych odpowiedziach
samych zdjęć. Naprawił m.in. podwojoną ilość rożka, pomylone ceny jednostkowe,
liczbę litrów i cenę paliwa oraz wiele nazw/gramatur. W Lidlu odczytał poprawnie
19 zakupów, ich rabaty i ceny bez duplikatów oraz bez ostrzeżeń arytmetycznych.

Trzy błędne kwoty w wariancie **zdjęcie + OCR**, potwierdzone na oryginałach:

- `20261018_090713`: zgubiony rabat −0,03 zł, kwota paliwa 249,98 zamiast 249,95 zł.
- `20261018_090822`: cebula 0,38 zamiast 0,22 zł po rabacie −0,16 zł.
- `IMG_6882`: ziemniaki 3,51 zamiast 1,75 zł po rabacie −1,76 zł.

Walidator zgłosił niezgodności dla wszystkich trzech dokumentów. Nazwy i kategorie
nadal wymagają kontroli: lody z pizzerii otrzymały kategorię żywności, a dwa kabanosy
z `IMG_6883` zostały przypisane do kosmetyków. Żadnej z tych trzech kategorii model
nie oznaczył jako wymagającej sprawdzenia.

## Audyt dodatkowy: rzeczy, których suma nie wykrywa

Ten audyt jest **post hoc**, osobno od zamrożonych metryk głównych. Nie zmienia
predykcji, wzorców ani wyników eksperymentu.

W pięciu polach kaucji wariant zdjęcie + OCR podał liczbę tam, gdzie wzorzec ma
null: cztery pola ilości/cen dwóch kaucji na screenie `140234` i jedna cena kaucji
na screenie `140250`. Sprawdzenie wizualne pierwszego screena potwierdziło brak
wydrukowanych ilości i cen jednostkowych. Kwoty kaucji były prawidłowe. Sam null
we wzorcu nie zawsze dowodzi nieobecności, dlatego wszystkie takie pola trafiają
na listę do audytu, a nie do automatycznej zbiorczej etykiety „halucynacja”.

Istniejący ewaluator traktuje wzorcowe nulle jako `not_annotated`. Główne metryki
nie karzą więc takich dopowiedzeń, a walidator nie ma źródła pozwalającego je
odrzucić. Dodano osobny audyt pól uzupełnionych przy nullu we wzorcu.

Dwa dokumenty samego vision miały błędne liczby, które **przeszły walidację**:

- ORLEN: zamienione 35,55 l i 6,37 zł/l. Iloczyn pozostaje identyczny.
- `IMG_6885`: rabat piwa zmieniony z −3,76 na −2,76 zł, a fikcyjne −1,00 zł
  przeniesione na donut. Poszczególne pozycje są błędne, ale suma nadal się zgadza.

Walidator jest kontrolą spójności, nie niezależnym potwierdzeniem treści zdjęcia.
W dodatkowej mierze obejmującej dopasowanie zakupów, brak nadmiaru, ilości, ceny
jednostkowe, kwoty produktów, kwoty kaucji i sumy dokumentu zgodne z anotacjami,
pipeline miał 19/24, zdjęcie 17/24, a zdjęcie + OCR 21/24. Ta miara nie obejmuje
poprawności nazw, kategorii, dat, dopowiedzianych pól ani kompletności kwot przed
rabatem; nie oznacza „21 całkowicie poprawnych paragonów”.

Wariant z OCR pozostawił 31 pól `before_discount` pustych: 30 na pozycjach bez
rabatu i jedno na cebuli z rabatem. Brak niewydrukowanego `discount=0.00` to osobny
problem normalizacji, a nie dziesiątki źle odczytanych rabatów. Kwot nie uzupełniano
po eksperymencie, aby zachować surowy wynik i nie poprawiać metryki wstecz.

## Koszt i czas

**43 nowe wywołania, 0,048318795 USD**, wspólny limit 0,45 USD. Pięć odpowiedzi
vision wykorzystano z poprzedniego cache. Brak ponowień, wszystkie 43 próby mają
znany koszt i rozliczone rezerwacje.

| Zakres | Koszt API |
|---|---:|
| Nowe same zdjęcia: 19 dokumentów | 0,015236070 USD |
| Nowe zdjęcie + OCR: 24 dokumenty | 0,033082725 USD |
| Same zdjęcia dla wszystkich 24, wraz z pięcioma starymi odpowiedziami | 0,019230225 USD |
| Historyczne tekstowe wzbogacanie tych 24 dokumentów | 0,006368480 USD |

Zdjęcie + OCR kosztowało około 5,2× tyle co tekstowe wzbogacanie w tym zestawie,
ale wykonuje również ekstrakcję struktury. Koszty nie obejmują lokalnego OCR/CPU.
Rozliczanie `SettledBudget` zachowuje pełny limit przed każdym żądaniem, zwalnia
tylko niewykorzystaną część znanej opłaty i pozostawia pełną rezerwację po timeout
lub braku kosztu. Historia i łączna suma konserwatywnych granic pozostają w ledgerze.

Mediana nowych wywołań obrazu wyniosła 32,381 s; obrazu + OCR 31,619 s. Maksimum
wariantu z OCR wyniosło 231,543 s. To obecnie istotny koszt użytkowy mimo niskiej
ceny API. Nie porównujemy tych czasów z historycznym tekstowym wzbogacaniem jako
czasem całego lokalnego pipeline'u. Wykorzystano zapisany OCR, bez jego ponownego
wykonywania w pomiarze.

## Artefakty i następny krok

Kod: `workers/extraction_worker/experiments/gemma_multimodal.py`, właściwe kroki
pipeline'u `GemmaVisionStep` / `GemmaVisionOCRStep` i ten sam walidator.
Referencje: `workers/extraction_worker/experiments/fixtures/gemma-vision-v2/`.
Cały przebieg: `workers/extraction_worker/data/output/2026-10-25_134950_833tf2kp/`:

- `plan.json`, podsumowania żądań z hashami obrazów i referencji;
- odpowiedzi dostawcy i wyniki kroków dla obu wariantów;
- `comparison.json` / `.md` z zamrożonymi metrykami;
- `budget.json`, `posthoc-audit.json`, `development-audit.md`, `source-frame-audit.json`.

Weryfikacja końcowa: **314 testów kodu przeszło**. Wszystkie 48 odpowiedzi
odtworzono ponownie z cache w trybie bez dostępu do sieci. Metryki główne i podział
na nowe/poprzednie dokumenty pozostały identyczne, wszystkie hashe referencji
zgadzają się z zamrożonym planem. Dodatkowy koszt i liczba nowych wywołań: zero.
Odtworzenie: `workers/extraction_worker/data/output/2026-10-25_154132_0rff4jk2/`;
potwierdzenie w `replay-verification.json` pierwotnego przebiegu.

W tej próbie warto rozwijać wariant zdjęcie + OCR. Przed przełączeniem aplikacji
trzeba rozwiązać przypisywanie rabatów do pozycji, nieuzasadnione uzupełnianie pól
i błędy kategorii, a także zdecydować o akceptowalnym czasie przetwarzania.
Po zmianach potrzeba nowego zestawu dokumentów: obecne 24 są już obejrzane i nie
powinny być ponownie przedstawiane jako świeży holdout. Nie zmieniono domyślnej
ścieżki aplikacji ani wag modelu.
