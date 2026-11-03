# Grouping HTTP contract

Group create/list/get/update/administrator and DELETE are implemented.
Group membership and invitation operations are implemented. Manual settlement CRUD is implemented; banking links are not supported.
Authentication uses ICurrentUser; outsiders and former members get 404 for details.

## Group metadata, images and archive
Create/update support application/json and multipart/form-data (iconFile).
Description is optional plain text, trimmed, limited to 255 Unicode characters.
Omitted/null description on PUT clears it.
Icon is a preset key; IconStorageKey is a custom upload key. Requests cannot supply
IconStorageKey. PUT without an icon instruction preserves the existing image;
removeIcon=true clears it. Preset/file/remove instructions are mutually exclusive.
Uploads accept PNG/JPEG/WebP signatures and matching MIME, max 5 MiB.
Oversize returns 413, invalid format 415, storage failures 503.

GET /api/groups/icon-presets lists signed previews from GROUP_ICON_PRESET_PREFIX
(default icons/presets/groups/). Populate this prefix administratively; no preset
objects are created by the SQL seed. Old identifiers such as home remain legacy
values; clients should use a fallback until a new preset/upload is selected.
GET /api/groups/{groupId}/icon returns a signed URL to an active member.
URLs expire and are never persisted.

DELETE /api/groups/{groupId} archives for the active administrator (204, repeatable).
Archived groups are hidden from lists/details/icons and cannot be modified.
Members, expenses, invitations, aggregates and files remain unchanged.
There is no restore API.

On image replacement the old custom file is deleted after commit, best effort
with a 10-second timeout. Presets are never deleted. Uncertain database commits
retain the uploaded file; there is no durable orphan cleanup.

| Method | Path | Intended access |
| --- | --- | --- |
| POST | /api/groups | Authenticated user |
| GET | /api/groups | Current user’s groups |
| GET | /api/groups/{groupId} | Member |
| PUT | /api/groups/{groupId} | Administrator |
| GET | /api/groups/{groupId}/members | Member |
| GET | /api/groups/{groupId}/members/{userId} | Member |
| POST | /api/groups/{groupId}/leave | Current member |
| DELETE | /api/groups/{groupId}/members/{userId} | Administrator |
| GET | /api/groups/{groupId}/invitations | Administrator |
| POST | /api/groups/{groupId}/invitations | Administrator |
| DELETE | /api/groups/{groupId}/invitations/{invitationId} | Administrator |

All lists accept pageNumber (default 1), pageSize (default 20, maximum 100)
and declare Shared.PagedResult<T> responses. Group/member lists accept
membership=active|former|all (default active). Invitation lists accept optional
isActive (omitted means all); expiry and usage limits are separate fields.

Create group: name, currencyCode (three uppercase ASCII letters), optional icon.
Update group: name, optional description and icon instruction; omitted icon preserves it. Currency cannot
be edited. Administrator and user identity come from the current user.
Create invitation: maxUses > 0 and expiresAt with an offset. The service validates future expiry and generates tokens. The create response declares
invitation metadata and a token; list responses never expose tokens or hashes.

Intended success codes: create 201, reads/update 200, leave/removal/revoke 204.
Member removal will retain the row and aggregates with IsMember=false.
Invitation deletion will revoke with IsActive=false. Administrator departure or
self-removal must be blocked until administrator succession is supported.
These membership and invitation rules are enforced by their services.

DTOs are organized by Groups, Members and Invitations, one public type per file.
List requests inherit Shared.PagedQuery, including descending=false. Validation
of pagination is shared; repositories execute sorting and pagination.

## Settlement and administrator contracts

GET/POST `/api/groups/{groupId}/settlements` and GET/PUT/DELETE
`/api/groups/{groupId}/settlements/{settlementId}` describe externally performed
payments. Reads require active membership; writes require the payer or group
administrator. Both parties are editable; a non-administrator must be the payer
before and after an update. These permissions are enforced by the settlement service.

Settlement requests contain fromUserId, toUserId, amount, method (1/2/3),
settledAt with offset, and optional bankTransactionId. PUT replaces all fields;
omitted/null bankTransactionId clears the link. Amount must fit numeric(18,2),
be positive and have at most two fractional digits. Parties must differ.
List filters: fromUserId, toUserId, dateFrom/dateTo (inclusive timestamps), method.
Shared pagination applies; sorting will use settledAt then id, ascending unless
descending=true. Lists declare PagedResult<GroupSettlementResponse>.

POST `/api/group-invitations/accept` takes a token in the body and declares
GroupMemberResponse (200). Identity comes from ICurrentUser.
PUT `/api/groups/{groupId}/administrator` takes userId and declares GroupResponse
(200). The current administrator may transfer to another active member and
remains a member.
Creation declares 201, settlement reads/updates 200 and deletion 204.
Membership, invitation validity, aggregate updates and transaction coordination
are implemented. Bank linking remains subject to the limitations described below.

## Invitations (implemented)
POST/GET /api/groups/{groupId}/invitations and DELETE
/api/groups/{groupId}/invitations/{invitationId} require the current active admin.
Outsiders/former members/archived groups return 404, non-admin members 403.
Creation returns 201 with a random Base64URL token shown only once; only SHA-256
is stored. maxUses > 0; expiresAt must be in the future (no upper bound).
Lists sort expiresAt then id and expose no hashes/tokens. isActive filters
administrative revocation only, independently of expiry/exhaustion.
Revocation is idempotent (204).
POST /api/group-invitations/preview and /accept take { "token": "..." }.
All require authentication; responses have Cache-Control: no-store.
Preview returns group name, description, currency, icon/optional signed image,
expiry and remaining uses; no membership or finance data.
Accept returns GroupMemberResponse. Former members reactivate preserving
JoinedAt and aggregates. Already-active members return 200 without consumption
even if the recognized non-revoked token has expired/exhausted.
Unavailable tokens/groups return 404; malformed tokens return 400.
Actual joining increments useCount and notifies other current members atomically.
The shared Grouping/Notifications connection and transaction are required.

## Koordynacja zapisów Grouping — 2026-11-01
Wszystkie operacje zapisu Grouping korzystają ze scoped
IGroupingTransactionCoordinator. Zewnętrzny przepływ otacza operacje przez
ExecuteAsync(action, succeeded, cancellationToken); wywołania zagnieżdżone
współdzielą transakcję i nie zatwierdzają jej samodzielnie. succeeded dla
wyników serwisów oznacza status 2xx. Odmowa lub wyjątek zagnieżdżony blokuje
końcowy commit nawet po przechwyceniu błędu. Walidacyjne odmowy przed zapisem
są zgłaszane do koordynatora przez Reject.

Nie rozpoczynaj bezpośrednio transakcji EF przed wejściem do koordynatora:
taka transakcja jest odrzucana przed mutacją. SaveChangesAsync nadal zapisuje
wszystkie oczekujące zmiany kontekstu jako jedną jednostkę pracy. Operacje
na wspólnym połączeniu wykonuj kolejno, bez równoległego używania zakresu DI.

Po rollbacku zakończ użycie zakresu DI i utwórz nowe konteksty. Koordynator
odrzuca kolejne zapisy w zakończonej niepowodzeniem jednostce pracy.
Nie czyści globalnie ChangeTracker ani nie naprawia stanu encji w pamięci.
Notifications jest dołączany jawnie do tej samej transakcji i połączenia;
właściciel transakcji odpowiada za odłączenie.

AfterCommit służy usunięciu zastąpionych własnych ikon dopiero po końcowym
commicie. AfterRollback sprząta nowe uploady tylko po potwierdzonym rollbacku.
Przy niepewnym wyniku commitu zachowujemy pliki. Cleanup jest best effort,
nie zmienia potwierdzonego sukcesu. Brak trwałej kolejki/outboxa.

## Członkostwo grup — 2026-11-01
GroupMemberService obsługuje listę, szczegóły, opuszczenie i usunięcie członka.
Listy obsługują active/former/all (domyślnie active), paginację i kierunek
sortowania po UserId. Odczyty dostępne aktywnym członkom niearchiwalnej grupy.
Szczegóły mogą dotyczyć także byłego członka, ale były członek nie odczytuje grupy.
Leave/Remove zmieniają tylko IsMember=false; historia, JoinedAt i wszystkie
agregaty pozostają. Niezerowe saldo nie blokuje operacji. Powtórzenie daje 204,
brak członkostwa 404; administrator musi wcześniej przekazać rolę (409).
Usunięcie nie jest banem: ważne zaproszenie pozwala wrócić. Brak nowych powiadomień.
Zapisy korzystają z GroupingTransactionCoordinator, blokując najpierw grupę.
Nie ma migracji ani obliczania/utrzymywania sald w tym serwisie.

## Rozliczenia ręczne i agregaty — 2026-11-02
GroupSettlementService implementuje CRUD rozliczeń wykonanych poza aplikacją.
Aktualizuje wkład przez osobny GroupMemberAggregateService we wspólnej transakcji.
Obie strony mogą być historycznymi członkami; wykonujący musi być aktualnym
członkiem i nadawcą albo administratorem. Przy edycji zwykły użytkownik pozostaje
nadawcą przed i po zmianie. Data nie może być przyszła, kwota nie jest ograniczana
saldem. BankTransactionId niepusty daje 400; Banking i wyłączność źródeł pozostają
na osobną turę. Powiązane historyczne rekordy można odczytać lub usunąć; PUT
z null usuwa istniejące powiązanie.

IGroupMemberAggregateService udostępnia ApplySettlementChangeAsync i
ApplyExpenseChangeAsync(previous,current). Null po jednej stronie oznacza
utworzenie/usunięcie; obie null są błędem. Kontrakty wartości należą do Grouping,
bez encji Expense. Płatnik/udziały aktualizują TotalPaid/TotalShare, nadawca/odbiorca
TotalSettlementsSent/Received. Różnice są scalane per użytkownik i sprawdzane
przed mutacją; wynik nie może być ujemny ani przekraczać numeric(18,2).
Historyczne członkostwa są zachowane, brakujące nie są tworzone.
Serwis wymaga aktywnego koordynatora (EnsureActive), blokuje grupę, nie robi
SaveChanges ani commitu. Wywołanie musi nastąpić dokładnie raz ze zmianą źródła.
Przyszły Expense musi stosować tę samą transakcję i blokadę grupy.
Nie podłączono ExpenseDbContext ani CRUD Expense. Brak odbudowy/uzgadniania
agregatów oraz nowych powiadomień. Nie ma migracji.

## Prywatność powiązania bankowego rozliczenia — 2026-11-02
Grouping zależy od publicznego IBankingReadService, bez zależności odwrotnej.
Lista, szczegóły i odpowiedzi zapisów ujawniają BankTransactionId tylko po
poprawnym odczycie dla ID zalogowanego użytkownika (nie nadawcy rozliczenia).
Administrator grupy nie uzyskuje uprawnień do cudzej bankowości. Brak odczytu
lub kontrolowany błąd Banking daje null bez ukrywania samego rozliczenia.
Nieoczekiwane wyjątki propagują się bez fallbacku ujawniającego ID.
Sprawdzenia listy są kolejne, z lokalnym cache dla jednej odpowiedzi.
Brak nowego interfejsu przypisań, tabeli i migracji. POST/PUT nadal odrzucają
niepuste BankTransactionId. Edycja/null i DELETE historycznych powiązań
pozostają dozwolone; wymagają zastąpienia claim/release przy wdrożeniu Banking.
Walidacja wskazuje konkretne pola zamiast wspólnego komunikatu bankowego.

## Podział migracji Grouping i Expense — 2026-11-02
Aktualny koniec łańcucha:
1. 20261102215301_AddGroupingModel — docelowy model Grouping i zachowane Limits.
2. 20261102215328_AddTemporaryExpenseAggregationSupport — tymczasowe minimum Expense.

Zastępują usunięte migracje 20261028191002_ApprovedGroupingModel i
20261031165114_AddGroupDescriptionIconsAndArchive. Wcześniejsze opisy tych
migracji są historyczne; nie wykonuj ich procedur cofania na nowym łańcuchu.

Expense zachowuje Id/_id, Name/name, TransactionTime/transaction_time,
fk_group_id, fk_receipt_id, fk_bank_transaction_id i udziały z fk_expense_id,
fk_user_id. Tymczasowe rozszerzenia: FkPayerId/fk_payer_id (wymagany,
indeks ix_expense_fk_payer_id, FK User) oraz Amount/amount numeric(18,2)
zamiast Percentage. Nie ma zmian nazw kolumn niezwiązanych z agregacją.
Grouping nie zależy od encji Expense; jego ExpenseContribution nadal używa
PaidByUserId jako nazwy kontraktu wartości.

Migracja Grouping nie zmienia tabel Expense. Jej Up/Down wymagają pustych
grup i członkostw. Tymczasowa Expense wymaga pustych wydatków/udziałów przy
Up/Down; nie konwertuje historycznych danych ani nie zgaduje płatnika.
Seed działa dopiero po obu migracjach i przelicza agregaty z danych syntetycznych.

Przy integracji właściwego PR Expense należy uzgodnić model, relacje, repozytoria,
tymczasową migrację, designer/snapshot, seed i testy. Samo usunięcie ostatniej
migracji nie cofa zastosowanego schematu. Lokalna baza po starym łańcuchu wymaga
kontrolowanego odtworzenia (SQL init → migracje → seed), bez kasowania kontenera
lub wolumenu. Nie zmieniaj ręcznie pojedynczych wpisów historii EF.

## Sugestie rozliczeń — 2026-11-02
GET /api/groups/{groupId}/settlements/suggestions: aktywny członek
niearchiwalnej grupy. Spójny odczyt grupy, autoryzacji i wszystkich agregatów
w jednym SQL AsSingleQuery/AsNoTracking, bez blokad zapisów i transakcji
koordynatora. Uwzględnia także byłych członków. Saldo = Paid - Share + Sent -
Received. Suma musi wynosić dokładnie zero, a końcowe liczniki Sent/Received
muszą mieścić się w numeric(18,2); w przeciwnym razie 409.
Osobny kalkulator stosuje deterministyczne największy dłużnik/największy
wierzyciel (kolejki priorytetowe, remisy po UserId), O(n log n), decimal.
Nie gwarantuje minimum; DTO jawnie zwraca optimalityGuaranteed=false.
Sugestie są odczytem i nie stanowią zlecenia płatności ani zapisów rozliczeń.
Bez migracji, Banking, odczytów Expense i odbudowy agregatów.
Kontrakt frontendowy: agent-context/grouping-frontedn.md.

## Granice dostępu do danych — 2026-11-02
Serwisy biznesowe wykonują odczyty i operacje na zbiorach przez repozytoria.
GroupRepository obsługuje również listę grup z informacją o członkostwie,
odczyt dla aktywnego członka oraz blokadę grupy. GroupMemberRepository pobiera
ID aktywnych członkostw dla powiadomień; Notifications filtruje aktywność kont.
SaveChangesAsync pozostaje w serwisach, a transakcje w GroupingTransactionCoordinator.
Wewnętrzne wyniki serwisów to GroupingResult<T> lub GroupingResult bez payloadu.
Kontrolery nadal zwracają dotychczasowe DTO, kody statusu i ProblemDetails;
wrapper wyniku nie jest częścią JSON. Usunięto nieużywany GroupingControllerBase.

## Settlement notifications — 2026-11-03
Creating a settlement notifies only ToUserId when different from the actor.
Former members with active accounts remain eligible; NotifyAsync skips inactive
accounts without failing the settlement. Updates and deletions do not notify.
SettlementReceived (5) references the group, without an expense reference.
The settlement, aggregates, notification and recipient share the coordinator's
transaction; a notification failure rolls back all writes.
The notification type CHECK is extended in AddGroupingModel, not in the temporary
Expense migration. Its Down maps type 5 to General before restoring types 0–4.
Editing an already-applied migration does not update an existing database;
applying the revised chain requires separately coordinating that environment.
