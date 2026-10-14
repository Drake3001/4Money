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

Obecna konfiguracja łączy przygotowanie, docscan i RapidOCR. UVDoc pozostaje
dostępny w eksperymencie preprocessingu. Modele ładują się
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

Audyt v2 obsługuje `x`, `×` i `*`, łączy fragmenty wiersza zakupowego według
geometrii i przypisuje każdy fragment do najwyżej jednej dopasowanej nazwy.
Usuwa przy porównaniu tylko zgodny ze wzorcem, osobno wydrukowany kod podatku.
Markę z `store` sprawdza w nagłówku pierwszej strony; brak dopasowania nie
oznacza automatycznie błędu OCR ani nie identyfikuje prawnej nazwy sprzedawcy.
Pole pozycji `total_is_printed: false` oznacza niewydrukowaną kwotę końcową.
Nie jest ona liczona jako odczyt OCR: osobna kontrola sprawdza sumę odczytanej
kwoty przed rabatem i odczytanego rabatu. Bez tego pola zachowujemy dotychczasowe
założenie, że kwota jest wydrukowana. `missing` oznacza brak przypisania przez
audyt, a nie potwierdzony brak wartości w surowym tekście OCR.

## Zbiór testowy z Google Drive

`dataset.py` pobiera nowe JPEG/PNG/PDF z publicznego folderu Drive i sprawdza
ręcznie przygotowane wzorce. Domyślny link wskazuje folder Receipts użytkownika.
Nie wymaga przeglądarki, OAuth ani cookies. Zależność jest opcjonalna:

```powershell
uv pip install --python .venv/Scripts/python.exe -e ".[dataset]"
```

Z katalogu repozytorium:

```powershell
# Pobierz nowe zdjęcia i przygotuj raport dla agenta.
.\workers\extraction_worker\dataset.ps1

# Sprawdź opisy po ręcznym uzupełnieniu, bez internetu.
.\workers\extraction_worker\dataset.ps1 -CheckOnly
```

Można też uruchomić `dataset.py` przyciskiem Run Python File, z interpreterem
workera, albo przez `.venv/Scripts/python.exe dataset.py`. Opcje Python:
`--check-only`, `--drive-url`, `--input-dir`, `--report-dir`.
Wrapper PowerShell przyjmuje `-DriveUrl`, jeśli zmieni się folder.

Raport `.cache/dataset/report.md` zawiera listę zadań dla agenta, pełne ścieżki
zdjęć i błędy pól, np. `items[0].quantity`. `report.json` zawiera te same dane
do dalszego przetwarzania. JSON-y nie są generowane automatycznie: agent lub
użytkownik przepisuje zdjęcia, użytkownik sprawdza treść, a skrypt ponownie
sprawdza strukturę. Brak TXT jest informacją, nie błędem blokującym.
Kod wyjścia 1 oznacza braki, błędy struktury/układu albo problem pobierania;
0 oznacza brak takich problemów, nie potwierdzenie treści opisów.
`review_status: "pending_user_review"` oznacza opis przygotowany przez agenta,
który użytkownik ma jeszcze sprawdzić. Raport pokazuje te przypadki osobno;
po sprawdzeniu można ustawić `review_status: "reviewed"`. Status nie blokuje
eksperymentów i nie jest potwierdzeniem jakości OCR.

W `deposits` kwoty są dodatnie. Gdy e-paragon nie drukuje ilości lub ceny
jednostkowej kaucji, `quantity` i `unit_price` mają wartość `null`, a wydrukowana
kwota pozostaje w `total`. Opcjonalne `deposit_returns` zapisuje zwroty opakowań
z ujemnymi `unit_price`/`total`; nie należy ich dodawać do rabatów produktów.
Kwota płatności obejmuje sumę produktów, kaucje wydane i ujemne zwroty.

Układ danych (taki sam na Drive i lokalnie, z opisami przechowywanymi lokalnie):

```text
data/input/
  pojedynczy.jpg
  pojedynczy.expected.json
  pojedynczy.expected.txt
  dlugi-paragon/
    01.jpg
    02.jpg
  dlugi-paragon.expected.json
  dlugi-paragon.expected.txt
```

Dla folderu jeden JSON opisuje cały paragon. Ma `source: "dlugi-paragon"`
i `sources: ["01.jpg", "02.jpg"]`, w kolejności alfabetycznej zgodnej z runnerem.
Stosuj numery z zerami wiodącymi. Gdy zdjęcia mają wspólne fragmenty, każdą
pozycję zakupową przepisz raz. Dodanie/usunięcie zdjęcia wykrywa walidator,
a ocena OCR pomija wzorzec z nieaktualną listą `sources`. Luźne zdjęcia zachowują
dotychczasowy format. Głębsze podfoldery są zgłaszane jako nieobsługiwane.

Skrypt pobiera tylko media; nie pobiera opisów z Drive i nie nadpisuje lokalnych
zdjęć ani JSON-ów. Pobrania są sprawdzane przed umieszczeniem w `input`, błędy
pojedynczego pliku nie zatrzymują pozostałych. JPEG z dodatkowym obrazem MPO
(np. mapa HDR z telefonu) jest obsługiwany; przygotowanie OCR używa głównego obrazu.

Stan w `.cache/dataset/sync-state.json` przechowuje ID Drive i SHA-256 lokalnych
plików. Istniejące zdjęcia są przy pierwszym uruchomieniu przyjmowane bez
ponownego pobierania — skrypt nie potwierdza, że ich bajty są identyczne z Drive.
Różne ID pod tą samą nazwą, duplikaty nazw oraz lokalne zmiany są zgłaszane.
Przyjmujemy, że zdjęć na Drive nie podmieniasz w miejscu: zmiany treści pod tym
samym ID nie są wykrywane bez ponownego pobierania. Zmieniony materiał dodaj jako
nowy przypadek z nową nazwą. Skrypt nie usuwa lokalnych danych po usunięciu na Drive.

Publiczne listowanie `gdown` zależy od strony Google i może przestać działać lub
trafić na limit pobrań. Taki błąd trafia do raportu, a lokalna walidacja nadal działa.
Uruchamiaj jedną synchronizację naraz. Zdjęcia, wzorce, raporty i stan pozostają
w katalogach ignorowanych przez Git.

## Testy

Uruchomienie z katalogu workera:

```powershell
New-Item -ItemType Directory -Force .cache/test-temp | Out-Null
.\.venv\Scripts\python.exe -m pytest -q --basetemp .cache/test-temp/pytest
```

Testy wykorzystują generowane obrazy, PDF i symulowane modele. Nie wymagają prywatnych
danych ani pobierania modeli; pliki tymczasowe pozostają poza katalogami danych.
