# Wymienne kroki analizy paragonów

Po uporządkowaniu z 2026-10-25 oba warianty analizy korzystają z `Pipeline`:
deklarują typ wejścia/wyjścia, wersję, konfigurację i zwracają `StepResult`.
Pipeline zapisuje czasy, ostrzeżenia i pierwszy błąd; po błędzie nie uruchamia
dalszych kroków. Eksperymenty i etykiety oceny pozostają w `experiments/`.

## Dwie ścieżki, jeden wynik

```text
OCRResult → ParseReceiptContextStep → ParsedReceipt
          → ClassifyReceiptItemsStep → AnalyzedReceipt → ValidateAnalyzedReceiptStep

SourceFiles → AnalyzeReceiptImagesStep → AnalyzedReceipt → ValidateAnalyzedReceiptStep

ImageOCRInput → AnalyzeReceiptImagesWithOCRStep → AnalyzedReceipt → ValidateAnalyzedReceiptStep
```

`ParsedReceipt` zachowuje paragon i jego OCR dla następnego kroku. Nie korzystamy
z globalnego „ostatniego OCR”. `ParseReceiptContextStep` może opakować istniejący
parser regułowy lub inny krok `OCRResult → ReceiptData`, np. parser Gemini.

`AnalyzedReceipt` zawiera:

- `receipt: ReceiptData` — odczytane nazwy i kwoty, ilości, rabaty, kaucje oraz
  rzeczywiste dowody OCR, jeżeli występują.
- `annotations: tuple[ItemAnnotation, ...]` — dla każdej pozycji jej `id`,
  `name_raw`, `name_corrected`, `category`, `needs_review`, `review_reason`.
  Nazwy poprawione są propozycjami; nie nadpisują odczytu. Liczba, kolejność i surowe
  nazwy adnotacji muszą odpowiadać pozycjom paragonu.
- `image_evidence: tuple[ImageEvidence, ...]` — deklarowane przez model ścieżki pól
  i numery zdjęć. Nie udają odwołań do fragmentów OCR i nie dowodzą poprawności.

`ClassifyReceiptItemsStep` wysyła maksymalnie 8 pozycji w jednym żądaniu, zachowuje
powtarzające się produkty i przelicza lokalne identyfikatory na globalne.
Przy podziale przenosi odwołania do właściwych fragmentów OCR. Pusty paragon nie
powoduje wywołania API. Niewłaściwa liczba lub kolejność odpowiedzi kończy krok
błędem; częściowe adnotacje nie stają się ukończonym wynikiem.

Walidacja liczb jest nadal w `validate_receipt`. `ValidateReceiptStep` obsługuje
dotychczasowy `ReceiptData`, a `ValidateAnalyzedReceiptStep` ten sam walidator
stosuje do `AnalyzedReceipt.receipt` i zachowuje adnotacje. Niczego nie poprawia
automatycznie. Ostrzeżenia to część `PipelineResult.warnings`; poprawne wykonanie
kroków (`ok`) nie oznacza, że paragon nie wymaga sprawdzenia.

## Składanie pipeline'u

Nazwy kroków opisują zadania; obiekt zadaniowy wybiera model i korzysta z klienta API:

| Ścieżka | Kroki | Klasa wykonująca analizę |
|---|---|---|
| Pełna tekstowa | `prepare → orient → ocr → parse → classify → validate` | `ClassifyReceiptItemsStep` |
| Zdjęcia | `analyze → validate` | `AnalyzeReceiptImagesStep` |
| Zdjęcia + zapisany OCR | `analyze → validate` | `AnalyzeReceiptImagesWithOCRStep` |

Klasyfikacja obejmuje również propozycje korekt nazw. Analiza obrazów odczytuje
strukturę paragonu i klasyfikuje produkty w jednym wywołaniu modelu. Fabryki
pipeline'ów zachowują nazwy, a integracje mają osobne klienty API. Nowe raporty i
`capture_steps` używają `classify` oraz `analyze`; stare raporty zachowują dawne
nazwy. Nie ma aliasów dawnych nazw klas kroków.

Przykłady zakładają, że aplikacja dostarczyła `api_key`. Samo zbudowanie klienta API, zadania
i pipeline'u nie wysyła żądania.

```python
from extraction.analysis import ReceiptItemClassifier, ReceiptImageAnalyzer
from extraction.providers import OpenRouterClient, GoogleClient, NvidiaClient
from extraction.workflows import text_analysis_pipeline, vision_analysis_pipeline

api = OpenRouterClient(api_key)
classifier = ReceiptItemClassifier(api, allow_paid=True, max_output_tokens=2048)
text = text_analysis_pipeline(classifier)
# result = text.run(saved_ocr_result)

analyzer = ReceiptImageAnalyzer(api, allow_paid=True)
vision = vision_analysis_pipeline(analyzer)
# result = vision.run(source_files)
```

Wariant obrazy + OCR ma osobny, jawny kontrakt wejścia i fabrykę:

```python
from extraction.contracts import ImageOCRInput
from extraction.workflows import vision_ocr_analysis_pipeline

hybrid = vision_ocr_analysis_pipeline(analyzer)
# result = hybrid.run(ImageOCRInput(source_files, saved_ocr_result))
```

Kolejność stron OCR musi odpowiadać obrazom. Wysyłamy tekst, pewność i ramki
znormalizowane do 0..1000, bez nazw plików, wyników parsera i referencji. Instrukcja
traktuje OCR jako omylne dane pomocnicze. Model nadal zwraca deklaracje źródeł
obrazowych, a nie fikcyjne dowody poprawności liczb. Oba warianty obrazu korzystają
z tego samego kontraktu wyjścia i walidatora; cache rozróżnia pełną treść żądania.

Gdy potrzebny jest pełny przebieg od plików do OCR i analizy:

```python
from main import build_pipeline

full_text = build_pipeline(classifier=classifier)
full_vision = build_pipeline(image_analyzer=analyzer)
```

Domyślne `build_pipeline()` nadal buduje lokalną ścieżkę prepare → orient → OCR →
parse → validate z wynikiem `ReceiptData`. Nie zmieniamy domyślnego kontraktu
istniejących odbiorców. Wybranie zadania analizy jawnie przełącza wynik na `AnalyzedReceipt`.
Próba jednoczesnego przekazania obu zadań jest błędem konfiguracji.

## Podział odpowiedzialności

```text
ClassifyReceiptItemsStep → ReceiptItemClassifier → OpenRouterClient / GoogleClient / NvidiaClient
AnalyzeReceiptImagesStep → ReceiptImageAnalyzer → OpenRouterClient
AnalyzeReceiptImagesWithOCRStep → ReceiptImageAnalyzer → OpenRouterClient
GeminiParseStep → GoogleClient
```

`extraction/steps/analysis.py` składa wynik kroku i dzieli produkty na paczki.
`extraction/analysis/item_classifier.py` oraz `receipt_images.py` przygotowują
żądania z promptami, sprawdzają odpowiedzi i zarządzają cache danego zadania.
`extraction/providers/` zawiera wyłącznie komunikację z dostawcami API: adresy,
uwierzytelnianie, timeouty i bezpieczne błędy. Klienci nie przechowują promptów,
cache ani danych ostatniego paragonu. Google i NVIDIA nie dziedziczą po OpenRouter.

Ten sam klient API może obsłużyć wiele zadań. Model, instrukcje, limity i wybór
płatnego trybu należą do konfiguracji zadania. Klasyfikacja OpenRouter domyślnie
wybiera dotychczasowy model `:free`; płatna klasyfikacja i analiza zdjęć wymagają
`allow_paid=True`. Dozwolone modele, routing i limity cenowe pozostały takie same.
Google i NVIDIA nadal mają osobne endpointy, klucze i cache, bez przełączania
żądania na innego dostawcę.

```python
google = GoogleClient(google_api_key)
google_classifier = ReceiptItemClassifier(google)
from extraction.steps.parsing import GeminiParseStep
gemini_parser = GeminiParseStep(google, model="gemini-3.1-flash-lite")

nvidia_classifier = ReceiptItemClassifier(NvidiaClient(nvidia_api_key))
```

Składanie pełnego przebiegu używa teraz `build_pipeline(classifier=...)` lub
`build_pipeline(image_analyzer=...)`. Konstruktor `GeminiParseStep` otrzymuje
`GoogleClient` zamiast surowego klucza. Importy klientów i zadań zostały
zmigrowane bez aliasów dawnych klas. Konfiguracja kroku zapisuje osobno `task`,
`client` i `provider`, obok modelu i metadanych promptu. Historycznych raportów
nie przepisujemy.

## Prompty: lokalizacja i zmiany

Względem `workers/extraction_worker/` instrukcje znajdują się w pakiecie
`extraction/prompts/`:

| Plik | Zastosowanie |
|---|---|
| `classify_items.md` | Kategorie produktów i propozycje korekt nazw, dla wszystkich dostawców Gemma |
| `analyze_receipt_images.md` | Odczyt i klasyfikacja ze zdjęć |
| `analyze_receipt_images_with_ocr.md` | Odczyt i klasyfikacja ze zdjęć z pomocniczym OCR |
| `ocr_guidance.md` | Instrukcja towarzysząca tekstowi i geometrii pomocniczego OCR |
| `parse_receipt_ocr.md` | Parser Gemini: struktura paragonu z OCR |

Pliki zawierają instrukcje, a nie kopie dynamicznych kategorii i schematów.
`extraction.prompts` dołącza kategorie z `categories.py` oraz schemat vision
z kodu parsera w dotychczasowej kolejności. Gemini nadal przekazuje swój schemat
osobno, w konfiguracji żądania. Loader korzysta z `importlib.resources` i działa
również po instalacji pakietu, niezależnie od bieżącego katalogu.

Nie usuwamy odstępów ani końcowych znaków nowej linii: wpływają na treść żądania
i jego klucz cache. Po edycji domyślnych plików uruchom proces ponownie.
Eksperymentalny dodatek do korekty nazw jest w
`experiments/prompts/name_policy.md`; benchmark nadal składa warianty `baseline`
i `candidate` jawnie, bez zmiany domyślnej instrukcji.

Własny prompt można przekazać przy tworzeniu obiektu zadaniowego:

```python
from pathlib import Path

custom = Path("my_classification_prompt.md").read_text(encoding="utf-8")
classifier = ReceiptItemClassifier(OpenRouterClient(api_key), allow_paid=True, prompt=custom)
workflow = text_analysis_pipeline(classifier)

# Oddzielne pełne instrukcje systemowe dla obu wariantów vision:
analyzer = ReceiptImageAnalyzer(
    OpenRouterClient(api_key), allow_paid=True, prompt=custom_image_prompt, ocr_prompt=custom_image_ocr_prompt,
    ocr_guidance=custom_ocr_guidance,
)
```

`prompt=None` wybiera domyślną instrukcję. Własny tekst zastępuje całą gotową
instrukcję, również dołączone kategorie i schemat vision; nie wyłącza walidacji
odpowiedzi. `ReceiptItemClassifier` dla Google/NVIDIA i `GeminiParseStep` również przyjmują
`prompt=`. Funkcje `build_request` przyjmują `prompt=`, a `build_vision_request`
także `ocr_prompt=` i `ocr_guidance=`. Pusty tekst lub same białe znaki są błędem.

Zadanie skonfiguruj przed zbudowaniem pipeline'u; do kolejnego wariantu utwórz
nowy obiekt zadaniowy i pipeline. Klient API może być współdzielony. Metadane kroku zawierają `prompt_id` i
`prompt_sha256` rzeczywistej instrukcji systemowej, już po renderowaniu.
Wariant z OCR zapisuje również `ocr_guidance_id` i `ocr_guidance_sha256`.
Domyślne identyfikatory odpowiadają nazwom plików bez `.md`, własne mają `custom`.
Hashe nie zawierają danych dokumentu. Zmiana promptu zmienia istniejący klucz
cache przez zmianę treści żądania; format kluczy nie został zmieniony.

Runner zapisuje wspólny wynik do JSON bez kluczy i bajtów obrazów. Ocena parsera
przyjmuje zarówno dotychczasowe paragony, jak i wspólny wynik — w pamięci i po
odczycie JSON. Ocenia odczyt z `receipt`; ocena proponowanych nazw i kategorii
pozostaje osobnym zadaniem benchmarków klasyfikacji.

## Budżet i cache przez jawny interfejs

Klasyfikator i analizator obrazów przyjmują `request_policy(body, send)`. Polityka jest uruchamiana tylko
przy braku odpowiedzi w cache; `send(body)` wykonuje pojedyncze żądanie.
Nie podmieniamy `_generate` w kodzie eksperymentów.

```python
import json
from extraction.requests import BudgetedRequests, cache_only
from experiments.budget import Budget

budget = Budget(new_run_directory, limit="0.45", max_calls=5)
policy = BudgetedRequests(
    budget,
    lambda body: ReceiptItemClassifier.request_bound(json.loads(body)),
)
classifier = ReceiptItemClassifier(
    OpenRouterClient(api_key), allow_paid=True, request_policy=policy,
)

# Odtwarzanie już zapisanych wyników bez możliwości wysłania żądania:
offline_analyzer = ReceiptImageAnalyzer(
    OpenRouterClient("offline-cache-only"), allow_paid=True, cache_dir=vision_cache_directory, request_policy=cache_only,
)
```

Rezerwacja następuje przed wysłaniem. Nieudana próba zachowuje rezerwację i licznik;
otrzymana odpowiedź rozliczana jest również wtedy, gdy jej JSON zostanie później
odrzucony. Cache nie zwiększa kosztu ani liczby prób. `cache_only` odrzuca cache miss
przed transportem. Vision korzysta z osobnej konserwatywnej funkcji `vision_bound`,
a nie estymatora tokenów tekstowych.

Ledger eksperymentu jest osobnym modułem `experiments.budget`. Istniejący ledger
wczytuje się przez `Budget.restore(directory)`; konstruktor nie pozwala nadpisać
go nowym, wyzerowanym budżetem. Jest to sekwencyjny budżet pojedynczego eksperymentu,
bez współbieżnego rozliczania wielu procesów.

Większy eksperyment `gemma_multimodal` korzysta z osobnej klasy `SettledBudget`:
po otrzymaniu znanego kosztu rozlicza daną próbę i zwalnia niewykorzystaną część
rezerwacji. Licznik prób i ich historia pozostają, a timeout lub brak raportowanego
kosztu pozostawia pełną rezerwację. Działanie starej klasy `Budget` pozostaje
kumulatywne. Wspólna polityka `BudgetedRequests` obsługuje oba ledgery.

`experiments.gemma_vision` przyjmuje jawny `--baseline-run`; nie ma w kodzie na stałe
wybranej daty starego uruchomienia. Pozwala też przekazać manifest, referencje, cache
i katalog wyjściowy. Używa standardowej ścieżki vision. Starsze benchmarki nadal
zachowują swoje formaty raportów do porównań historycznych i korzystają z klientów,
wspólnego dzielenia pozycji oraz jawnych polityk żądań.

## Sprawdzenie refaktoryzacji

Odtworzono offline wszystkie 24 dokumenty / 115 pozycji poprzedniego benchmarku
przez tekstowy pipeline. Odczyty parsera i wszystkie adnotacje są identyczne
z zapisanym wariantem baseline. Nowe żądania były zabronione przez `cache_only`.
Ślad: `workers/extraction_worker/data/output/2026-10-25_131536_5p6zglj9/refactor-verification.json`.

Pięć zapisanych odpowiedzi vision przeszło nowy pipeline, zachowując wyniki
wcześniejszego porównania; zero nowych wywołań i kosztu. Ślad:
`workers/extraction_worker/data/output/2026-10-25_130947_1tsqf8ec/comparison.json`.

Testy obejmują rzeczywiste składanie kroków, podział powtarzających się zakupów,
zachowanie kwot, ostrzeżeń i pochodzenia danych, błędy odpowiedzi, zapis JSON,
blokadę sieci przy odtwarzaniu, cache i limity nieudanych wywołań. Refaktoryzacja
nie poprawia jakości predykcji modelu i nie zmienia dawnych wyników pomiaru.
