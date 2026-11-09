# Migracje

Aktualna procedura i wymagania: [praca w repo](../agent-context/development.md#modele-i-migracje).
Skróty uruchamiaj z katalogu głównego repozytorium:

```bash
dotnet tool restore
dotnet run --file tools/ef.cs -- migrations list
dotnet run --file tools/ef.cs -- migrations has-pending-model-changes
dotnet run --file tools/ef.cs -- migrations add NAZWA
dotnet run --file tools/ef.cs -- database update
```

Nowa baza wymaga SQL init → istniejące migracje → opcjonalny seed.
Nie twórz nowej migracji InitialMigration, nie czyść historycznych Up/Down
ani historii EF w celu naprawy istniejącej bazy. Dalsze notatki o scaffoldingu
są materiałem pomocniczym, a nie standardową procedurą tworzenia migracji.

# lokalna baza danych
1. Skopiujcie `containers/local/.env.example` do `containers/local/.env`, jeśli plik jeszcze
   nie istnieje. Konfigurację całego stosu opisuje [instrukcja kontenerów](../containers/local/README.md).
   Dla bazy ustawcie:
   1. `POSTGRES_USER`
   2. `POSTGRES_PASSWORD`
2. potem w folderze `containers/local`: `docker compose up -d postgres-db`
3. pgadmin: servers -> register -> wypełniacie to (najlepiej zaznaczyć save password na przyszłość)
4. Z katalogu głównego odtwórzcie przypięte lokalne narzędzia: `dotnet tool restore`.

# modele - ogólne info
1. modele są wygenerowane komendami dotnet do orm
2. teraz można robić zmiany w kodzie i robić migracje i zaktualizuje się postawiona baza, do której jest połączenie
3. połączenie jest w ZPI4M.Api w Program.cs
4. musicie dodać .env w ZPI4M.Api: `DB_LOCAL_CONNECTION_STRING=Host=localhost;Port=5434;Database=zpi4m_db;Username=TWOJ_USER;Password=TWOJE_HASLO`

## modele - jakby coś się zepsuło
1. do modułów, które posiadają modele należy dodać 2 paczki (drugie też w `ZPI4M.Api` oraz oba w `ZPI4M.Database`):
   1. `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3`
   2. `dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.4`
2. generowanie modeli (przykład dla Expense - z poziomu folderu `src/modules/ZPI4M.Expense`):
   1. ustawić w terminalu zmienne na czas sesji, przykład powershell:
      1. `$env:POSTGRES_USER="TWOJ_USER"`
      2. `$env:POSTGRES_PASSWORD="TWOJE_HASLO"`
   2. `dotnet ef dbcontext scaffold "Host=localhost;Port=5434;Database=zpi4m_db;Username=$env:POSTGRES_USER;Password=$env:POSTGRES_PASSWORD" Npgsql.EntityFrameworkCore.PostgreSQL --schema expense --context ExpenseDbContext --output-dir Models --context-dir Database --force --no-onconfiguring`
3. po wygenerowaniu, należy dodać przedrostek Models. w dwóch miejscach, które visual podkreśli na czerwono w poniższych plikach:
   1. `ZPI4M.Expense/Database/ExpenseDbContext.cs`
   2. `ZPI4M.Receipt/ReceiptDbContext.cs`
   3. `ZPI4M.User/userDbContext.cs`
4. Dodatkowo aby nasze podejście z jednym `AppDbContext` do migracji działało trzeba stworzyć (niestety ręcznie) tzw konfiugracje modeli:
   1. wyciąć zawartośc metody `OnModelCreating` z db kontekkstu dla danego modułu
   2. stworzyć plik `ZPI4M.Expense/Database/ExpenseConfiguration.cs` - `public static class ExpenseConfiguration`
   3. w stworzonej klasie dodać metodę `public static void Configure(ModelBuilder modelBuilder)` i tam wkleić zawartość tego co było wycięte w 1 punkcie
   4. po operacjach metoda z 1 punktu powinna wyglądać tak:
      ```
      protected override void OnModelCreating(ModelBuilder modelBuilder)
	   {
         base.OnModelCreating(modelBuilder);
         ExpenseConfiguration.Configure(modelBuilder);
         OnModelCreatingPartial(modelBuilder);
	   }
      ```

## Klucz pliku paragonu

Migracja `RenameReceiptS3LinkToStorageKeyAndMakeUnique` zmienia nazwę
kolumny `receipt.receipt.s3_link` na `storage_key` i dodaje unikalny
indeks `ux_receipt_storage_key`. Kolumna pozostaje wymagana (`NOT NULL`).

Pole przechowuje trwały klucz zdjęcia paragonu w magazynie plików,
służący do generowania czasowego adresu dostępu.

Skrypty `database-init` tworzą schemat początkowy z kolumną `s3_link`.
Po utworzeniu bazy należy zastosować migracje EF, aby uzyskać aktualny
schemat. Nie zmieniamy tej nazwy w skryptach inicjalizujących ani
w historycznych migracjach.
