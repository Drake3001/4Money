# Extraction worker — notatka dla zespołu

Moduł rozwijamy jako pipeline: przygotowanie → OCR → parser → klasyfikacja.
Obecnie działają przygotowanie, OCR, parser regułowy i walidacja; klasyfikacja jest kolejnym etapem.
Krótki opis modułu: [README](../workers/extraction_worker/README.md).

## 1. Środowisko i pierwszy start

Używamy **uv ≥ 0.12.23**. uv dobiera Python 3.12, tworzy `.venv` i instaluje paczki
z `uv.lock`. Nie trzeba osobno tworzyć ani aktywować środowiska. Python, paczki
i brakujące modele przy pierwszym użyciu mogą wymagać pobrania.

Instalacja uv według [oficjalnej instrukcji](https://docs.astral.sh/uv/getting-started/installation/):

Windows, PowerShell:
```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

Linux, Bash:
```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

Po instalacji otwórz nowy terminal. Dalej polecenia uv są wspólne dla obu systemów.
Z głównego katalogu repozytorium:
```sh
cd workers/extraction_worker
uv sync
uv run main.py
```

Pierwsze uruchomienie utworzy `data/input/`, jeśli go nie ma. Dodaj dokumenty
i uruchom ponownie. Domyślnie działa `prepare → RapidOCR → parse → validate`, z `EXPERIMENT = None`.
Kolejne przykłady wykonuj z katalogu workera. `uv.lock` zapisujemy w Git;
środowiska, zdjęcia, modele i wyniki pozostają lokalne.

Instalację i uruchomienie sprawdzono na Windowsie. Dla Linuxa sprawdzono rozwiązanie
zależności i składnię przykładów; uruchomienie na serwerze wymaga jeszcze weryfikacji.

## 2. Dane, wyniki i ustawienia

Jeden plik to jeden dokument. Bezpośredni podfolder grupuje zdjęcia paragonu:
```text
data/input/
  paragon.jpg
  dlugi-paragon/
    01.jpg
    02.jpg
```
Obsługujemy JPEG, PNG i PDF. Kolejność plików wynika z nazw, a stron PDF z dokumentu.
Każdy przebieg ma nowy katalog `data/output/<przebieg>/`. Dla dokumentu zapisujemy
`result.json` oraz po udanym OCR `ocr.json` i `ocr.txt`; podsumowanie jest w `summary.md`.
W `ocr.json` pole `pages[].lines[]` zawiera tekst, confidence i cztery punkty `box`.
Ramki dotyczą obrazu po przygotowaniu/preprocessingu; wymiary i źródła są zachowane.

Konfiguracja jest w `main.py`, bez argumentów CLI:

| Ustawienie | Znaczenie |
|---|---|
| `build_pipeline()` | Kroki zwykłego przebiegu i ich kolejność |
| `INPUT_DIR`, `OUTPUT_DIR` | Katalogi wejścia i wyników |
| `EXPERIMENT` | `None`, `"preprocessing"` lub `"ocr_comparison"` |
| `OCR_VARIANTS`, `OCR_REPEATS` | Silniki porównania i liczba mierzonych powtórzeń |
| `PDF_DPI`, `DEBUG` | Renderowanie PDF i zapis obrazów po krokach |
| `TESSERACT_COMMAND`, `TESSDATA_DIR` | Program Tesseract i jego modele językowe |

Program czyta cały `INPUT_DIR`. Do sprawdzenia zmian wybierz osobny katalog
z 2–3 zdjęciami, jeden wariant i `OCR_REPEATS = 1`.
W VS Code otwórz repozytorium i użyj konfiguracji **Extraction worker** (F5/Ctrl+F5).
Przy **Run Python File** wybierz interpreter `.venv` workera.

## 3. Co jest gdzie i jak działa krok

`main.py` składa pipeline, `runner.py` czyta dokumenty, a `reporting.py` zapisuje wyniki.
`contracts.py` definiuje przekazywane dane. `pipeline.py` wywołuje kroki kolejno,
mierzy czas i zapisuje błędy. Każdy krok udostępnia `run(input) → StepResult`.

Wszystkie adaptery OCR dziedziczą wspólną obsługę stron z `steps/ocr/common.py`:
```text
Pipeline.run(document)
  → LocalOCRStep.run(document)
      → initialize() konkretnego silnika
      → recognize(content) dla każdej strony
      → OCRResult z tekstem, confidence i ramkami
```
Adapter odpowiada za konfigurację i wywołanie biblioteki. RapidOCR, Paddle i EasyOCR
używają ponownie załadowanego silnika; Tesseract uruchamia program dla każdej strony.
Adaptery OCR nie zapisują raportów ani nie interpretują produktów i kwot.

`ParseReceiptStep` z `steps/parsing/` otrzymuje `OCRResult` i zwraca `ReceiptData`.
Łączy tekst z geometrią, oddziela zakupy, rabaty, kaucje i zwroty oraz odczytuje
sumę towarów i kwotę do zapłaty. Nie korzysta ze wzorców ani modeli uczonych.
`total` oznacza towary po rabatach; `amount_due` uwzględnia kaucje i zwroty.
Braki pozostają `null`; powtórzone produkty pozostają osobnymi pozycjami.
`evidence` przypisuje polom indeksy fragmentów OCR oraz metodę `observed` lub `derived`.
Walidacja zgłasza rozbieżności sum i wartości niefinitywne, bez zmiany danych.

`experiments/evaluation.py` ocenia odczyt względem ręcznego wzorca. Wykorzystuje
nazwy produktów i geometrię do przypisywania cen, więc ma elementy parsera
wspomaganego wzorcem. Nie jest parserem aplikacji. Błąd dopasowania
w audycie nie dowodzi błędu OCR. Ta ocena nie zmienia surowych fragmentów w wyniku.

## 4. Eksperymenty i opcjonalne silniki

Do zwykłego pipeline’u wystarcza `uv sync`. Pozostałe paczki instalujemy na żądanie.
Przy `uv run` powtarzaj wybrane `--extra`, żeby uv zachowało potrzebne dodatki:

```sh
# Paddle: paddle_mobile lub paddle_server_det
uv sync --extra ocr-paddle
uv run --extra ocr-paddle main.py

# EasyOCR; torch i torchvision są pobierane z indeksu CPU
uv sync --extra ocr-easyocr
uv run --extra ocr-easyocr main.py

# Docscan i UVDoc
uv sync --extra experiment
uv run --extra experiment main.py

# Wszystkie biblioteki porównania OCR (Tesseract instalujemy osobno)
uv sync --extra ocr-paddle --extra ocr-easyocr --extra ocr-comparison
uv run --extra ocr-paddle --extra ocr-easyocr --extra ocr-comparison main.py
```

Najpierw wybierz tryb w `main.py`. `"preprocessing"` porównuje `original`, `docscan`
i `uvdoc` z tym samym RapidOCR. `"ocr_comparison"` porównuje wybrane `OCR_VARIANTS`:
`rapidocr`, `paddle_mobile`, `paddle_server_det`, `easyocr`, `tesseract`.
Przygotowanie jest wspólne, a każdy silnik działa kolejno w osobnym procesie.
Wyniki są w katalogach wariantów; porównanie daje `comparison.md` i `comparison.json`.
Pomiary są w `timings.json`. Awaria procesu kończy wariant; następne silniki działają.

Inicjalizację i rozgrzewkę mierzymy osobno. Rozgrzewka odczytuje dodatkowo pierwszą
poprawną stronę: dwa jednostronicowe dokumenty i pięć silników to 10 odczytów
mierzonych oraz 5 rozgrzewek. Porównanie wszystkich silników na CPU może trwać długo.
Mediana jest dostępna, jeśli wszystkie powtórzenia się powiodły; jakość oceniamy
na pierwszym odczycie. Przerwanego eksperymentu nie wznawiamy.

Używamy jednego OpenCV: `opencv-python-headless`. Konfiguracja uv wyklucza inne
dystrybucje nadpisujące `cv2`; nie trzeba ręcznie przeinstalowywać OpenCV.
Wersje modeli i ustawienia silników pozostają w adapterach. Paddle ma wyłączone
MKLDNN z powodu błędu używanej wersji na Windowsie; nie przełączamy go automatycznie.

## 5. Tesseract 5

Windows: zainstaluj Tesseract 5 według [instrukcji Windows](https://github.com/UB-Mannheim/tesseract/wiki).
Dodaj program do PATH lub ustaw `TESSERACT_COMMAND` w `main.py`. Istniejący lokalny
`.cache/tools/tesseract/tesseract.exe` jest wybierany automatycznie.

Linux — **przykład dla Ubuntu/Debian**, nie instrukcja dla każdej dystrybucji:
```bash
sudo apt-get update
sudo apt-get install -y tesseract-ocr
tesseract --version
```
Wymagana jest wersja 5. Domyślne `TESSERACT_COMMAND = "tesseract"` używa PATH.
Dystrybucja i architektura serwera wymagają potwierdzenia; nie zakładamy wsparcia ARM/GPU.

Na obu systemach używamy modeli `pol` i `eng` z [tessdata_best](https://github.com/tesseract-ocr/tessdata_best),
w katalogu wskazanym przez `TESSDATA_DIR`. Pobranie w PowerShell:
```powershell
New-Item -ItemType Directory -Force .cache/models/tesseract | Out-Null
Invoke-WebRequest https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/main/pol.traineddata -OutFile .cache/models/tesseract/pol.traineddata
Invoke-WebRequest https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/main/eng.traineddata -OutFile .cache/models/tesseract/eng.traineddata
```
Odpowiednik w Bash:
```bash
mkdir -p .cache/models/tesseract
curl -fL https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/main/pol.traineddata -o .cache/models/tesseract/pol.traineddata
curl -fL https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/main/eng.traineddata -o .cache/models/tesseract/eng.traineddata
```

## 6. Wzorce, dataset i testy

Opcjonalny `paragon.expected.json` opisuje oczekiwane produkty i kwoty dla
`paragon.jpg`; dla folderu podaj jego nazwę w `source` i kolejność plików w `sources`.
Audyt OCR zapisuje się w `evaluation/report.md` i `report.json`.
Porównanie OCR zawiera audyt we własnym raporcie. Tylko `review_status: "reviewed"`
oznacza sprawdzony wzorzec; porównanie rozdziela go od wzorców niezatwierdzonych.

Osobna ocena parsera znajduje się w `parsing-evaluation/report.md` i `report.json`.
Porównuje gotowe predykcje, zachowując kolejność oraz powtórzenia produktów;
liczy brakujące i nadmiarowe pozycje. Kwoty porównuje dokładnie jako Decimal,
a nazwy raportuje osobno jako zgodne, przybliżone, różne albo brakujące.
Raport zachowuje status zatwierdzenia i `uncertain_fields` wzorca.
Brak tekstu w OCR ogranicza wynik parsera; nie naprawiamy go danymi ze wzorca.

Do iteracji nad parserem można odtworzyć zapisany odczyt bez uruchamiania OCR:
```sh
uv run python -m experiments.parsing data/output/<przebieg>/rapidocr data/input data/output/<nowy-przebieg-parsera>
```
Katalog wyjściowy musi być nowy. Odtwarzanie przyjmuje starsze `result.json`
z wynikiem OCR oraz nowe `ocr.json`; zachowuje ramki i metadane bez czytania zdjęć.

CER to odległość między odczytanym tekstem a transkrypcją, podzielona przez długość
transkrypcji. Wymaga `transcription_scope: "full_image"` w JSON i pełnego
`*.expected.txt`, ze wszystkimi widocznymi tekstami stron, także powtórzeniami.
Brak pełnego wzorca oznacza „nie oceniono”. `ocr-comparison` przyspiesza CER przez rapidfuzz.

Pobieranie nowych zdjęć z Drive i sprawdzanie lokalnych wzorców:
```sh
uv sync --extra dataset
uv run --extra dataset dataset.py
uv run --extra dataset dataset.py --check-only
```
Skrypt nie nadpisuje lokalnych zdjęć ani wzorców. Raport jest w `.cache/dataset/`.

Testy z generowanymi obrazami i symulowanymi silnikami, bez pobierania modeli:
```sh
uv sync --extra test
uv run --extra test pytest -q --basetemp .cache/test-temp/pytest
```
