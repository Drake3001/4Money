# lokalna baza danych
1. musicie dodać swój .env do containers, dodjacie tam dwie zmienne środowiskowe:
   1. `POSTGRES_USER`
   2. `POSTGRES_PASSWORD`
2. potem w folderze continers: `docker-compose up - d`
3. pgadmin: servers -> register -> wypełniacie to (najlepiej zaznaczyć save password na przyszłość)
4. potem zainstalujcie sobie (jeśli nie macie) EF globalnie, bo to się przyda do migracji: `dotnet tool install --global dotnet-ef`

# modele - ogólne info
1. modele są wygenerowane komendami dotnet do orm
2. teraz można robić zmiany w kodzie i robić migracje i zaktualizuje się postawiona baza, do której jest połączenie
3. połączenie jest w ZPI4M.Api w Program.cs
4. musicie dodać .env w ZPI4M.Api: `DB_LOCAL_CONNECTION_STRING=Host=localhost;Port=5434;Database=zpi4m_db;Username=TWOJ_USER;Password=TWOJE_HASLO`

## modele - jakby coś się zepsuło
1. do modułów, które posiadają modele należy dodać 2 paczki (drugie też w `ZPI4M.Api` oraz oba w `ZPI4M.Database`):
   1. `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3`
   2. `dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.4`
2. generowanie modeli (przykład dla Expense - z poziomu folderu `src/ZPI4M.Expense`):
   1. ustawić w terminalu zmienne na czas sesji, przykład powershell:
      1. `$env:POSTGRES_USER="TWOJ_USER"`
      2. `$env:POSTGRES_PASSWORD="TWOJE_HASLO"`
   2. `dotnet ef dbcontext scaffold "Host=localhost;Port=5434;Database=zpi4m_db;Username=$env:POSTGRES_USER;Password=$env:POSTGRES_PASSWORD" Npgsql.EntityFrameworkCore.PostgreSQL --schema expense --context ExpenseDbContext --output-dir Models --context-dir Database --force --no-onconfiguring`
3. po wygenerowaniu, należy dodać przedrostek Models. w dwóch miejscach, które visual podkreśli na czerwono w poniższych plikach:
   1. `ZPI4M.Expense/Database/ExpenseDbContext.cs`
   2. `ZPI4M.Receipt/ReceiptDbContext.cs`
   3. `ZPI4M.User/userDbContext.cs`

# migracje - CO KAŻDY ROBI LOKALNIE
1. jak zrobicie połączenie z bazą to musicie zarejestrować pierwszą migrację
2. z poziomu folderu `ZPI4M.Backend/src`, rejestrujecie stan bazy z ostatniej dostępnej migracji (initial będzie pusta - rejestrowanie połączenia z bazą):
    `dotnet ef database update --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext`

# migracje - ogólnie
1. migracje trafiają do folderu `ZPI4M.Database/Migrations`, obok pliku `AppDbContext` (ten plik rejestruje wszystkie konfiguracje modeli)
2. robimy zmiany w kodzie modeli C# (zawsze trzeba też spojrzeć do db context i tam coś dodać, najprostszy case z nową kolumną to po prostu dopisanie kolejnego `entity.Property().HasColumnName()` - i tu wpisujemy snake_case żeby zachować konwencję w bazie) i potem migrujemy, z poziomu `ZPI4M.Backend/src`:
    `dotnet ef migrations add AddExpenseDescription --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext --output-dir Migrations`
3. zatwierdzenie migracji:
    `dotnet ef database update --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext`

## migracje - od zera jakby coś się zepsuło
1. jak zrobicie połączenie z bazą to musicie zrobić pierwszą migrację
2. z poziomu folderu `ZPI4M.Backend/src`:
    `dotnet ef migrations add InitialMigration --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext --output-dir Migrations`
3. wchodzicie do `InitialMigration.cs` i USUWACIE zawartości metod `Up()` i `Down()` - mają istnieć ale mieć psute w {}, bo baza już istnieje przecież
4. z poziomu folderu `ZPI4M.Backend/src`, rejestrujecie pustą migrację initial w bazie (to jest stworzenie połączenie po prostu, w bazie nic się nie zmieni):
    `dotnet ef database update --project ZPI4M.Database --startup-project ZPI4M.Api --context AppDbContext`