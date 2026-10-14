# Konfiguracja pre-commit

Hooki sprawdzają pliki przed commitem i pomagają zachować wspólne formatowanie.
Instalujemy je osobno w każdym lokalnym klonie repozytorium. Poniższe polecenia
uruchamiaj z katalogu głównego repozytorium; działają w Bash oraz PowerShell.

## Wymagania

- Git i [uv](https://docs.astral.sh/uv/getting-started/installation/).
- SDK .NET 10 lub nowszy zgodny z projektem, jeśli zmieniasz pliki C#.
- Dostęp do internetu przy pierwszej instalacji zależności i środowisk hooków.

Możesz sprawdzić dostępność narzędzi poleceniami `git --version`, `uv --version`
i `dotnet --version`. Narzędzia Python wymagają wersji 3.10 lub nowszej; uv może
pobrać zgodny interpreter. Nie trzeba samodzielnie instalować pre-commit przez pip.

## Instalacja

```bash
uv sync --project tools --only-group pre-commit --locked
uv run --project tools --only-group pre-commit --locked python tools/hooks/install.py
```

Instalator konfiguruje trzy etapy: `pre-commit`, `prepare-commit-msg` oraz
`commit-msg`. Nie nadpisuje własnych hooków ani konfiguracji `core.hooksPath`.
Zachowaj katalog `tools/.venv`: zainstalowane hooki korzystają z tego środowiska.
Nie musisz go aktywować przed każdym commitem. Pierwsze uruchomienie hooków
może potrwać dłużej, ponieważ pobierane są ich środowiska.

Opcja `--locked` wymaga zgodności zależności z `tools/uv.lock`. Po pobraniu zmian
w zależnościach ponownie wykonaj powyższe polecenia. Jeśli celowo zmieniasz
zależności, zaktualizuj lockfile przez `uv lock --project tools`, przejrzyj zmiany,
a następnie zsynchronizuj środowisko i zainstaluj hooki ponownie.

## Przygotowanie backendu

Po dodaniu lub scaleniu backendu wykonaj:

```bash
dotnet restore backend/ZPI4M.Backend/ZPI4M.Backend.slnx
```

Powtórz restore po zmianach paczek NuGet albo referencji między projektami.
Hook C# używa `--no-restore`, więc nie pobiera paczek samodzielnie. Przed scaleniem
backendu możesz zainstalować pozostałe hooki; bez plików C# formatter nie uruchamia .NET.

## Co dzieje się przy commicie

- `pre-commit` poprawia końcowe białe znaki i brakujące końce linii, sprawdza YAML,
  znaczniki konfliktów oraz obecność kluczy prywatnych. Zachowuje podwójne spacje
  oznaczające łamanie linii w Markdown. Dla przekazanych plików C# uruchamia
  `dotnet format style` i `dotnet format whitespace`, zgodnie z `backend/.editorconfig`.
- `prepare-commit-msg` dodaje identyfikator zadania ClickUp z nazwy brancha.
- `commit-msg` sprawdza, czy po identyfikatorze zadania znajduje się opis.

Przykład brancha: `CU-869fb6e1g/Imie-Nazwisko`. Wiadomość `Dodaj walidację`
zostanie przygotowana jako `CU-869fb6e1g - Dodaj walidację`. Identyfikator już
obecny w wiadomości nie zostanie dodany drugi raz; inny identyfikator powoduje błąd.
Na branchu bez identyfikatora hook wyświetla ostrzeżenie i pozostawia wiadomość.
Automatyczne wiadomości merge, squash, rebase i zachowane wiadomości amend
pozostają bez zmian. Hooki nie kontaktują się z API ClickUp.

Jeśli formatter zmieni pliki, commit zostanie zatrzymany. Przejrzyj różnice,
dodaj zamierzone zmiany do indeksu Git i ponów commit. Hooki nie wykonują `git add`.
Przy częściowo dodanych plikach sprawdź, czy ponownie dodajesz tylko odpowiednie
fragmenty. Konflikt poprawek formattera z niezapisanymi w indeksie zmianami może
wymagać ręcznego formatowania i ponownego przygotowania commita.

## Ręczne uruchomienie

Aby sprawdzić wszystkie śledzone pliki, również po zmianie EditorConfig:

```bash
uv run --project tools --only-group pre-commit --locked pre-commit run --all-files
```

Polecenie może zmienić pliki. Przejrzyj poprawki przed dodaniem ich do commita.
Etapy dotyczące wiadomości commita uruchamiają się podczas tworzenia commita.

## Typowe problemy

- **Brak uv albo Git:** zainstaluj narzędzie i otwórz ponownie terminal, aby odświeżyć PATH.
- **Brak zgodnego SDK .NET:** zainstaluj SDK, nie tylko runtime. Sprawdź `dotnet --version`
  w katalogu rozwiązania oraz ewentualne wymagania `global.json`.
- **Błąd formatowania lub brak paczek:** uruchom restore, rozwiąż zgłoszony problem
  i ponów sprawdzenie. Restore może wymagać dostępu do NuGet.
- **Brak lub wiele rozwiązań .NET:** hook oczekuje dokładnie jednego pliku `.slnx`
  albo `.sln` pod `backend/`, poza katalogami generowanymi. Uporządkuj układ projektu.
- **Istniejący własny hook:** sprawdź wskazany plik i uzgodnij sposób połączenia
  konfiguracji. Nie usuwaj ani nie nadpisuj hooka bez sprawdzenia jego przeznaczenia.
- **Ustawiony `core.hooksPath`:** sprawdź `git config --show-origin --get core.hooksPath`.
  Ustal, kto zarządza tym katalogiem, zanim zmienisz konfigurację.
- **Usunięte lub przeniesione `tools/.venv`:** wykonaj ponownie synchronizację
  i instalację hooków.

## Hooki a CI

Lokalny hook C# nie wykonuje jawnego builda ani testów aplikacji. Poprawne
formatowanie nie oznacza, że backend się kompiluje lub że jego testy przechodzą.

CI osobno sprawdza higienę repozytorium oraz wykonuje restore, build Release,
sprawdzenie formatowania i testy backendu. CI nie uruchamia testów hooków.
Projekty testowe pod `backend/ZPI4M.Backend/tests/` muszą być dodane do rozwiązania
oraz kończyć się na `.UnitTests.csproj` albo `.IntegrationTests.csproj`.
W każdym projekcie uruchamiane są wszystkie testy, bez wymogu kategorii.
Brak projektów oznacza „not configured”; istniejący projekt z zerową liczbą
wykonanych testów powoduje błąd. Raporty TRX są przechowywane przez siedem dni.

Hooki można lokalnie pominąć, dlatego nie zastępują kontroli CI.
