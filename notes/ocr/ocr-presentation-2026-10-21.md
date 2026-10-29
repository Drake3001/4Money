# OCR — materiał do prezentacji 21.10.2026

## Proponowana historia (7 slajdów, około 8–10 minut)

1. **Od zdjęcia do danych paragonu.** Cel: obsłużyć rzeczywiste zdjęcia i wybrać rozsądny wariant OCR. Wynikiem OCR są tekst i ramki, dalej przetwarzane przez parser.
2. **Pipeline umożliwia kontrolowane eksperymenty.** Schemat: wejście JPG/PNG/PDF → przygotowanie → opcjonalna obróbka → orientacja → wymienny OCR → parser → walidacja. Pokazać warianty obok siebie: prepare → RapidOCR; prepare → Docscan → RapidOCR; prepare → UVDoc → RapidOCR. Aktualny main.py zawiera orientację; benchmark z 16.10 jej nie obejmował. Klasyfikacja jest planowana.
3. **Czy prostowanie pomaga?** Ten sam paragon: oryginał / Docscan / UVDoc, pod każdym ten sam fragment wyniku OCR. Wskazać konkretnie przycięcie, deformację lub utraconą kwotę, jeżeli występują na wybranym przykładzie. Wniosek ograniczyć do pokazanych przypadków: obróbka nie gwarantuje poprawy. Ładniejszy obraz nie musi dawać lepszego odczytu.
4. **Trudny przypadek z QR.** Zdjęcie, ramki OCR i błędny wynik. Rozróżnić awarię procesu od dodatkowego tekstu rozpoznanego w kodzie QR lub błędnego kadrowania. Przyczynę nazwać dopiero po sprawdzeniu konkretnego przykładu. Sam benchmark nie ustala przyczyny problemu z QR.
5. **Pięć wariantów na wspólnych danych: czas i jakość audytu.** Tabela poniżej albo dwa wykresy: mediana czasu dokumentu oraz zgodne pola liczbowe. Na slajdzie umieścić informację o wstępnych wzorcach i pojedynczym pomiarze.
6. **Rodzaj wejścia i sposób oceny zmieniają obraz wyników.** Zdjęcia kontra screenshoty; przykład EasyOCR ze spacjami w kwotach. To temat o ograniczeniach ewaluacji i wymaganiach integracji OCR.
7. **Decyzja i następne eksperymenty.** RapidOCR jako szybka baza; Paddle mobile jako kandydat do poprawy trudnych przypadków. Większy detektor daje niewielki dodatkowy wynik audytu przy dużym wzroście czasu. Następnie: sprawdzenie wzorców, pełne transkrypcje do CER, powtarzane pomiary i osobna ocena orientacji/prostowania. Selektywne ponawianie trudnych dokumentów w Paddle to hipoteza do przetestowania.

## Dane głównego porównania

Źródło: `workers/extraction_worker/data/output/2026-10-16_173846_h3o8p7e6/comparison.json` i `comparison.md`; konfiguracja i środowisko w `manifest.json`.

27 dokumentów: 21 zdjęć (nazwy 202610… oraz IMG…) i 6 screenshotów (Screenshot…). Wszystkie warianty ukończyły 27/27 dokumentów. To ukończenie wykonania, nie poprawny odczyt wszystkich danych.

| Wariant | Mediana czasu dokumentu [s] | Czas / RapidOCR | Zgodne pola liczbowe audytu | Udział zgodnych pól | Nazwy zgodne / przybliżone / brak przypisania |
|---|---:|---:|---:|---:|---|
| RapidOCR | 8,87 | 1,00× | 315/359 | 87,7% | 40 / 41 / 3 |
| Paddle mobile | 30,55 | 3,45× | 341/359 | 95,0% | 51 / 33 / 0 |
| Paddle server det | 134,53 | 15,17× | 345/359 | 96,1% | 48 / 36 / 0 |
| EasyOCR | 44,17 | 4,98× | 40/359 | 11,1% | 3 / 56 / 25 |
| Tesseract | 8,27 | 0,93× | 158/359 | 44,0% | 12 / 48 / 24 |

Wnioski do wypowiedzenia:

- Paddle mobile: o 26 zgodnych pól więcej niż RapidOCR, przy medianie czasu 3,45× większej.
- Paddle server det: tylko 4 zgodne pola więcej niż mobile, przy medianie czasu około 4,40× większej. W tych adapterach zmienia się detektor; rozpoznawanie pozostaje mobile latin PP-OCRv5.
- RapidOCR i Paddle współdzielą rodzinę PP-OCRv5. To porównanie skonfigurowanych rozwiązań, obejmujące runtime i pipeline, a nie pięciu niezależnych rodzin modeli.
- Łącznie 359 pól nie oznacza 359 niezależnych przypadków. Najdłuższy paragon `20261010_201603.jpg` wnosi 115 pól, około 32% całości. Nie utożsamiać udziału pól z odsetkiem poprawnych paragonów.

## Zdjęcia kontra screenshoty

Agregacja tych samych zapisanych wyników według nazw źródeł; licznik: audit.numeric_field_counts.match, mianownik: suma wszystkich kategorii numeric_field_counts.

| Wariant | Zdjęcia: zgodne pola / wszystkie | Udział | Screenshoty: zgodne pola / wszystkie | Udział |
|---|---:|---:|---:|---:|
| RapidOCR | 271/315 | 86,0% | 44/44 | 100% |
| Paddle mobile | 297/315 | 94,3% | 44/44 | 100% |
| Paddle server det | 301/315 | 95,6% | 44/44 | 100% |
| EasyOCR | 40/315 | 12,7% | 0/44 | 0% |
| Tesseract | 114/315 | 36,2% | 44/44 | 100% |

100% dotyczy wyłącznie 44 ocenianych pól na sześciu screenshotach; nie całego tekstu ani całego dokumentu. Różne dokumenty i treści nie pozwalają przypisać całej różnicy samemu sposobowi wykonania obrazu.

## Konkretny przykład ograniczenia audytu

Przypadek: `Screenshot_20261011_140219_appka.jpg`, zapisany w głównym comparison.json.

| Fragment | RapidOCR | EasyOCR |
|---|---|---|
| Pozycja | `2szt. x2.99 5.98C` | `2szt _ x2 . 99 5 .98C` |
| Rabat | `-2.00C` | `-2 .00c` |
| Suma | `SUMA PLN 3.98` | `SUMA PLN 3 .98` |
| Wynik audytu pól | 4 zgodne | 4 brakujące |

Cyfry są widoczne także w wyniku EasyOCR. Spacje i segmentacja utrudniają interpretację kwot. Wynik audytu 0/4 nie dowodzi, że model nie odczytał żadnej cyfry. Z tego powodu niski zbiorczy wynik EasyOCR wymaga analizy błędów przed oceną jakości samego silnika. Nie wykonano tu poprawki ani ponownej oceny z normalizacją spacji.

## Ograniczenia liczb — stopka lub notatki prelegenta

- Wszystkie 27 wzorców w porównaniu ma status `unreviewed`. Wyniki są wstępne.
- To audyt dopasowania nazw i pól liczbowych, wspomagany wzorcem i geometrią. Błędne przypisanie może pogorszyć wynik. Nagłówek wykresu: „Zgodność pól liczbowych w audycie”, nie „Dokładność OCR”.
- CER nie został policzony dla żadnego przypadku: brak kwalifikujących się pełnych transkrypcji. Nie podawać procentu błędów znakowych.
- CPU, Windows 11, Python 3.12.15; manifest deklaruje cpu_threads=4. Pomiar obejmuje adapter OCR bez przygotowania, ewaluacji i zapisu. Po jednym mierzonym odczycie na dokument; liczba na slajdzie jest medianą między dokumentami.
- Przebieg był wznawiany. Część czasów inicjalizacji i rozgrzewki jest niedostępna, a skróty wejść/modeli potwierdzano przy wznowieniu. Czasy traktować jako orientacyjne.
- Tesseract uruchamia proces i ładuje modele dla każdej strony. Paddle miał wyłączone MKLDNN. Wnioski dotyczą tych konfiguracji na tym środowisku.

## Gdzie szukać materiałów wizualnych

Zapisane wcześniejsze przebiegi (sprawdzone kroki w result.json):

- `2026-10-12_203748_ib80onq4`: prepare → OCR.
- `2026-10-12_210507_vfvwc98y` i `2026-10-12_210945_i_lf2zff`: prepare → Docscan → OCR.
- `2026-10-12_211507_ag97c5pi`: prepare → Docscan → UVDoc → OCR.
- `2026-10-12_212200_fiz4jc_i`: prepare → UVDoc → OCR.

Katalog bazowy: `workers/extraction_worker/data/output/`. Do porównania wizualnego dobrać ten sam plik we wszystkich wariantach. Nie łączyć starych wyników audytu preprocessingu z nowym audytem jako jednolitego benchmarku: sam algorytm oceny zmieniał się w czasie, co dokumentuje `2026-10-14_130248_qollxa23/evaluation/comparison.md`.

Zdjęcia nie były w ramach tego opracowania oceniane wizualnie. Przykład QR i wnioski o deformacji wymagają wyboru oraz sprawdzenia konkretnych obrazów podczas składania slajdów.
