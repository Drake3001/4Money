# lokalna baza danych
1. musicie dodać swój .env to containers, dodjacie tam dwie zmienne środowiskowe:
   1. `POSTGRES_USER`
   2. `POSTGRES_PASSWORD`
2. potem w folderze continers: `docker-compose up - d`
3. pgadmin: servers -> register -> wypełniacie to (najlepiej zaznaczyć save password na przyszłość)
4. potem zainstalujcie sobie (jeśli nie macie) EF globalnie, bo to się przyd do migracji: `dotnet tool install --global dotnet-ef`

## modele - ogólne info
1. modele są wygenerowane komendami dotnet do orm
2. teraz można robić zmiany w kodzie i robić migracje i zaktualizuje się postawiona baza, do której jest połączenie
3. połączenie jest w ZPI4M.Api w Program.cs
4. musicie dodać .env w ZPI4M.Api: `DB_LOCAL_CONNECTION_STRING=Host=localhost;Port=5434;Database=zpi4m_db;Username=TWOJ_USER;Password=TWOJE_HASLO`

## modele - jakby coś się zepsuło
1. do modułów, które posiadają modele należy dodać 2 paczki (drugie też w `ZPI4M.Api`):
   1. `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3`
   2. `dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.4`
2. generowanie modeli (przykład dla Expense):
   1. ustawić w terminalu zmienne na czas sesji, przykład powershell:
      1. `$env:POSTGRES_USER="TWOJ_USER"`
      2. `$env:POSTGRES_PASSWORD="TWOJE_HASLO"`
   2. `dotnet ef dbcontext scaffold "Host=localhost;Port=5434;Database=zpi4m_db;Username=$env:POSTGRES_USER;Password=$env:POSTGRES_PASSWORD" Npgsql.EntityFrameworkCore.PostgreSQL --schema expense --context ExpenseDbContext --output-dir Models --context-dir Database --force --no-onconfiguring`
3. po wygenerowaniu, należy dodać przedrostek Models. w dwóch miejscach, które visual podkreśli na czerwono w poniższych plikach:
   1. `ZPI4M.Expense/Repositories`
   2. `ZPI4M.Receipt/Repositories`
   3. `ZPI4M.User/Repositories`

# migracje - CO KAŻDY ROBI LOKALNIE
1. jak zrobicie połączenie z bazą to musicie zrobić zarejestrować migracje
2. z poziomu folderu `ZPI4M.Backend`, rejestrujecie stan bazy z ostatniej dostępnej migracji:
    `dotnet ef database update --project src/ZPI4M.User --startup-project src/ZPI4M.Api --context UserDbContext`

# migracje - ogólnie
1. migracje trafiają do folderu Database/Migrations, obok pliku db context - każdy db context ma swoje migracje (czyli każdy moduł ma migracje dotyczące tylko jego modeli)
2. robimy zmiany w kodzie modeli C# (zawsze trzeba też spojrzeć do db context i tam dodać, najprostszy case z nową kolumną to po prostu dopisanie kolejnego `entity.Property().HasColumnName()` - i tu wpisujemy snake_case żeby zachować konwencję w bazie) i potem migrujemy, przykład dla Expense:
    `dotnet ef migrations add AddExpenseDescription --project src/ZPI4M.Expense --startup-project src/ZPI4M.Api --context ExpenseDbContext`
3. zatwierdzenie migracji:
    `dotnet ef database update --project src/ZPI4M.Expense --startup-project src/ZPI4M.Api --context ExpenseDbContext`

## migracje - od zera
1. jak zrobicie połączenie z bazą to musicie zrobić pierwsze migracje
2. z poziomu folderu `ZPI4M.Backend`, dla każdego modułu z modelami, przykład dla User:
    `dotnet ef migrations add InitialBaseline --project src/ZPI4M.User --startup-project src/ZPI4M.Api --context UserDbContext --output-dir Database/Migrations`
3. wchodzicie do InitialBaseline.cs i USUWACIE zawartości metod `Up()` i `Down()` - mają istnieć ale mieć psute w {}, bo baza już istnieje przecież
4. z poziomu folderu `ZPI4M.Backend`, rejestrujecie pustą migrację baseline w bazie (to jest stworzenie połączenie po prostu, w bazie nic się nie zmieni):
    `dotnet ef database update --project src/ZPI4M.User --startup-project src/ZPI4M.Api --context UserDbContext`