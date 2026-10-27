# banking-refresh

Lokalna symulacja, 2026-10-25. Mock pochodzi z zapisanej kolekcji Tink Diagnostics,
kontraktu adaptera i TinkDataHttpTests; nie jest mockiem opublikowanym przez Tink.
Wyszukiwanie mocka właściciela pominięto przy niezalogowanym CLI.

## Purpose

Tink jest jedynym członkiem symulacji. Backend, worker, JWT i PostgreSQL działają
rzeczywiście. Powtarzalne błędy i blokada umożliwiają test retry, częściowego
sukcesu oraz odzyskania zadania po przerwaniu procesu.

## Execution

Pełna procedura i bezpieczny setup: [README](README.md).
`node postman/support/refresh-local.cjs simulation`, osobno `tls` i `backend`;
następnie `run 40 50 60 70 71 72 73 74`, `restart-test`, `verify`.
Logi Runnera są w ignorowanym `.cache/banking-refresh/runner-*.log`.
Uruchomienie CLI wyłącza publikację wyników i historię symulacji.

## Assumptions

- Backend wymaga HTTPS dla Tink. Osłona TLS i certyfikat zaufany wyłącznie przez
  proces sprawdziły się w rzeczywistych wywołaniach workera (2026-10-25).
- Dwa konta mają stabilne zdalne ID i po dwie operacje BOOKED z ostatnich dni;
  trzecia operacja sprzed 45 dni jest filtrowana. Reset atrapy nie resetuje bazy.
- API zwraca wyniki zadania, nie pełny zapis transakcji; snapshot i zapytania SQL
  są oddzielnym potwierdzeniem persystencji.

## Observations

<!-- begin:observations -->
2026-10-25: członek tink obsłużył tokeny, granty, wszystkie strony kont i transakcji,
403, 429 z Retry-After i blokadę. Foldery 40, 50, 60, 70–76 przeszły na backendzie
z PostgreSQL. Po SIGKILL to samo zadanie odzyskało lease, pomijając zapisane konto;
SQL potwierdził 4 transakcje i stałe ID podczas ponowienia; dodatkowo brak przesunięcia kursora błędnego konta, brak zmian po nieudanym discovery oraz zamknięcie i ponowne otwarcie rachunków. Brak runu Sandbox.
<!-- end:observations -->

## What didn't work

- 2026-10-25: Env.Load w startupie EF nadpisał zmienną połączenia. Omyłkowa
  AddBankRefreshes trafiła do bazy deweloperskiej; została cofnięta transakcyjnie
  po sprawdzeniu pustych nowych tabel i kolumn, bez zmiany rekordów danych.
  Helper używa teraz jawnego --connection, a backend czystego katalogu roboczego.
- 2026-10-25: CLI nie eksportuje zmienionych zmiennych środowiska między runami.
  Helper zachowuje tylko publiczne ID i snapshot wyniku do osobnego runu restartu;
  tokeny pozostają w lokalnym pliku środowiska z uprawnieniami 0600.
