# Extraction worker

Lokalny moduł OCR dla paragonów zapisanych jako JPEG, PNG lub PDF. Jeden paragon może
obejmować kilka zdjęć lub stron. Pipeline przygotowuje dokument, opcjonalnie przetwarza
obrazy i zwraca tekst, ramki oraz pewność rozpoznania. Parser danych paragonu, HTTP
i kolejka pozostają poza zakresem modułu.

## Uruchamianie i konfiguracja

Punktem wejścia jest `main.py`, uruchamiany w VS Code przez **Run Python File** lub
**F5 / Ctrl+F5** z konfiguracją **Extraction worker**. Konfiguracja debuggera wskazuje
interpreter `.venv/Scripts/python.exe`; Run Python File korzysta z interpretera
wybranego w VS Code. Program nie wymaga argumentów CLI.

Ustawienia w `main.py`:

| Element | Znaczenie |
|---|---|
| `build_pipeline()` | Lista kroków i ich kolejność |
| `INPUT_DIR`, `OUTPUT_DIR` | Katalogi danych względem lokalizacji workera |
| `PDF_DPI` | Rozdzielczość renderowania PDF; domyślnie 200 DPI |
| `DEBUG` | Zapis obrazów po poszczególnych krokach |
| `EXPERIMENT` | `None`: zwykły pipeline; `"preprocessing"`: porównanie wariantów |

Obecna konfiguracja łączy przygotowanie, docscan, UVDoc i RapidOCR. Modele ładują się
przy pierwszym użyciu i są ponownie wykorzystywane. Błąd paragonu nie zatrzymuje
pozostałych; kod wyjścia 1 sygnalizuje niepowodzenie.

## Środowisko

Python 3.12, `uv`; integracja z VS Code wymaga rozszerzeń Python i Python Debugger.
Instalacja w katalogu `workers/extraction_worker` (PowerShell):

```powershell
$env:UV_PYTHON_INSTALL_DIR = Join-Path (Get-Location) ".cache/python"
uv python install 3.12
uv venv --python 3.12 .venv
uv pip install --python .venv/Scripts/python.exe -e ".[test,ocr,experiment]"
uv pip install --python .venv/Scripts/python.exe --reinstall-package opencv-python-headless opencv-python-headless==5.0.0.93
```

Zestaw `experiment` obejmuje zależności docscan i UVDoc. Dla samego przygotowania i OCR
wystarcza `.[test,ocr]`. Ostatnie polecenie ustala runtime OpenCV po instalacji pakietów
współdzielących moduł `cv2`; wymaga zatrzymanego workera. Pierwsze użycie modeli wymaga
ich pobrania.

## Dane i raporty

Katalogi lokalne są ignorowane przez Git:

| Katalog | Zawartość |
|---|---|
| `data/input/` | Zdjęcia, PDF i opcjonalne wzorce `*.expected.json` / `*.expected.txt` |
| `data/output/` | Unikalny katalog każdego przebiegu |
| `data/archive/` | Historyczne wyniki w dotychczasowym układzie |
| `.cache/` | Modele i lokalny interpreter |

Luźny plik oznacza jeden paragon. Bezpośredni podfolder grupuje pliki jednego paragonu
według nazw; głębsze foldery nie są przeszukiwane. Strony PDF zachowują kolejność.
JPEG i PNG są normalizowane do RGB PNG z uwzględnieniem EXIF. Uszkodzony plik lub PDF
wymagający hasła przerywa przetwarzanie danego paragonu.

Każdy paragon otrzymuje `result.json` oraz, po OCR, `ocr.txt`. Przebieg zawiera
`summary.md` i `environment.json` z wersjami bibliotek. JSON zawiera OCR, ramki,
wymiary i pochodzenie stron, bez bajtów obrazów. Debug zapisuje obrazy w
`debug/<nazwa-kroku>/<numer-strony>.png`. Istniejące wyniki nie są nadpisywane.

## Pipeline i organizacja kodu

| Moduł | Odpowiedzialność |
|---|---|
| `extraction/contracts.py` | Kontrakty dokumentów, OCR i wyników |
| `extraction/pipeline.py` | Wykonanie kroków, kontrola typów, czasy i błędy |
| `extraction/runner.py` | Odczyt plików, grupowanie i uruchamianie paczki |
| `extraction/reporting.py` | Zapis raportów i obrazów debug |
| `extraction/steps/` | Przygotowanie, docscan, UVDoc, RapidOCR, demo i walidacja |
| `experiments/` | Porównanie preprocessingu i ocena względem wzorców |
| `tests/` | Testy kontraktów, kroków, runnera i raportowania |

Pipeline jest uporządkowaną listą nazwanych instancji. Przykład bez preprocessingu:

```python
def build_pipeline():
    return Pipeline([
        ("prepare", PrepareDocumentStep(pdf_dpi=PDF_DPI)),
        ("ocr", RapidOCRStep()),
    ])
```

Krok deklaruje `input_type`, `output_type`, `version`, `config` i udostępnia
`run(input) -> StepResult(output, warnings)`. Dodanie kompatybilnej implementacji
wymaga dopisania pary do listy, bez zmian runnera. Nazwy są unikalne, bezpieczne dla
katalogów Windows i zawierają wyłącznie litery ASCII, cyfry, `_` oraz `-`.

Kroki nie modyfikują wejść. Dokument przechowuje oryginalne źródła i niemutowalne bajty
obrazów; tablice bibliotek są lokalne. Ramki OCR odnoszą się do wymiarów obrazu po
preprocessingu, w pikselach od lewego górnego rogu. Pipeline przy błędzie zachowuje
ostatnie poprawne wyjście i wskazuje nieudany krok. Brak tekstu oznacza ostrzeżenie.
Raporty zapisuje runner, poza pipeline’em.

## Eksperymenty

Tryb `"preprocessing"` przygotowuje dokument raz i porównuje `original`, `docscan`
i `uvdoc` z jednym współdzielonym modelem OCR. Wyniki wariantów mają osobne katalogi;
`comparison.md` zawiera wspólne zestawienie. Docscan może pozostawić nieskorygowaną
perspektywę przy braku narożników, a UVDoc może zniekształcić treść.

Ocena korzysta z ręcznych wzorców luźnych zdjęć i tworzy `evaluation/report.md` oraz
`report.json`. Jest heurystycznym audytem dopasowania nazw i kwot, bez roli parsera
lub pomiaru CER/WER.

## Testy

Uruchomienie z katalogu workera:

```powershell
New-Item -ItemType Directory -Force .cache/test-temp | Out-Null
.\.venv\Scripts\python.exe -m pytest -q --basetemp .cache/test-temp/pytest
```

Testy wykorzystują generowane obrazy, PDF i symulowane modele. Nie wymagają prywatnych
danych ani pobierania modeli; pliki tymczasowe pozostają poza katalogami danych.
