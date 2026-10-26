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

Nazwy kroków opisują zadania, a dostawcę i model wybiera klient:

| Ścieżka | Kroki | Klasa wykonująca analizę |
|---|---|---|
| Pełna tekstowa | `prepare → orient → ocr → parse → classify → validate` | `ClassifyReceiptItemsStep` |
| Zdjęcia | `analyze → validate` | `AnalyzeReceiptImagesStep` |
| Zdjęcia + zapisany OCR | `analyze → validate` | `AnalyzeReceiptImagesWithOCRStep` |

Klasyfikacja obejmuje również propozycje korekt nazw. Analiza obrazów odczytuje
strukturę paragonu i klasyfikuje produkty w jednym wywołaniu modelu. Fabryki
pipeline'ów i nazwy klientów dostawców pozostają bez zmian. Nowe raporty i
`capture_steps` używają `classify` oraz `analyze`; stare raporty zachowują dawne
nazwy. Nie ma aliasów dawnych nazw klas kroków.

Przykłady zakładają, że aplikacja dostarczyła `api_key`. Samo zbudowanie klienta
i pipeline'u nie wysyła żądania.

```python
from extraction.steps.enrich import PaidOpenRouterEnricher
from extraction.steps.parsing.vision import GemmaVisionParser
from extraction.workflows import text_analysis_pipeline, vision_analysis_pipeline

text_client = PaidOpenRouterEnricher(api_key, max_output_tokens=2048)
text = text_analysis_pipeline(text_client)
# result = text.run(saved_ocr_result)

vision_client = GemmaVisionParser(api_key)
vision = vision_analysis_pipeline(vision_client)
# result = vision.run(source_files)
```

Wariant obrazy + OCR ma osobny, jawny kontrakt wejścia i fabrykę:

```python
from extraction.contracts import ImageOCRInput
from extraction.workflows import vision_ocr_analysis_pipeline

hybrid = vision_ocr_analysis_pipeline(vision_client)
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

full_text = build_pipeline(enricher=text_client)
full_vision = build_pipeline(vision_client=vision_client)
```

Domyślne `build_pipeline()` nadal buduje lokalną ścieżkę prepare → orient → OCR →
parse → validate z wynikiem `ReceiptData`. Nie zmieniamy domyślnego kontraktu
istniejących odbiorców. Wybranie klienta jawnie przełącza wynik na `AnalyzedReceipt`.
Próba jednoczesnego przekazania obu klientów jest błędem konfiguracji.

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

Własny prompt można przekazać przy tworzeniu klienta:

```python
from pathlib import Path

custom = Path("my_classification_prompt.md").read_text(encoding="utf-8")
client = PaidOpenRouterEnricher(api_key, prompt=custom)
workflow = text_analysis_pipeline(client)

# Oddzielne pełne instrukcje systemowe dla obu wariantów vision:
vision_client = GemmaVisionParser(
    api_key, prompt=custom_image_prompt, ocr_prompt=custom_image_ocr_prompt,
    ocr_guidance=custom_ocr_guidance,
)
```

`prompt=None` wybiera domyślną instrukcję. Własny tekst zastępuje całą gotową
instrukcję, również dołączone kategorie i schemat vision; nie wyłącza walidacji
odpowiedzi. Klienci Google/NVIDIA Gemma i `GeminiParseStep` również przyjmują
`prompt=`. Funkcje `build_request` przyjmują `prompt=`, a `build_vision_request`
także `ocr_prompt=` i `ocr_guidance=`. Pusty tekst lub same białe znaki są błędem.

Klienta skonfiguruj przed zbudowaniem pipeline'u; do kolejnego wariantu utwórz
nowego klienta i pipeline. Metadane kroku zawierają `prompt_id` i
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

Klienci przyjmują `request_policy(body, send)`. Polityka jest uruchamiana tylko
przy braku odpowiedzi w cache; `send(body)` wykonuje pojedyncze żądanie.
Nie podmieniamy `_generate` w kodzie eksperymentów.

```python
import json
from extraction.requests import BudgetedRequests, cache_only
from experiments.budget import Budget

budget = Budget(new_run_directory, limit="0.45", max_calls=5)
policy = BudgetedRequests(
    budget,
    lambda body: PaidOpenRouterEnricher.request_bound(json.loads(body)),
)
text_client = PaidOpenRouterEnricher(api_key, request_policy=policy)

# Odtwarzanie już zapisanych wyników bez możliwości wysłania żądania:
offline_client = GemmaVisionParser(
    "offline-cache-only", cache_dir=vision_cache_directory, request_policy=cache_only,
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
