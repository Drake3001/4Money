# Postman — integracja Tink

Kolekcje obejmują wyłącznie flow Tink i jego obsługę przez Banking.
Nie zawierają testów loginu, modułu User ani innych modułów aplikacji.

| Kolekcja | Środowisko | Przeznaczenie |
| --- | --- | --- |
| `4Money.Banking.postman_collection.json` | `environments/4Money.Local.example.postman_environment.json` | Rozpoczęcie próby, odczyt rezultatu, połączeń, zgód i błędy callbacku |
| `Tink.Diagnostics.postman_collection.json` | `environments/Tink.Sandbox.example.postman_environment.json` | Token aplikacji, token użytkownika, zgody oraz osobne delegowanie Tink Link |

Importuj kolekcję i właściwy szablon środowiska. Wartości poufne uzupełniaj
wyłącznie lokalnie. Kolekcje używają formatu v2.1 oraz aktualnego Postman Desktop
z obsługą `pm.execution.skipRequest`, `pm.execution.setNextRequest` i `URL`.

**Instrukcja zespołowa, sekwencje i zasady rozbudowy:** [notes/POSTMAN.md](../notes/POSTMAN.md).

Uruchamiaj foldery etapami. Pełny run zatrzymuje się przed ręcznym krokiem
bankowym; nie oznacza zaliczenia całego flow. Chronione żądania 4Money wymagają
rzeczywistego auth, którego backend jeszcze nie implementuje. Sam wpisany token
nie odblokuje hosta. Diagnostyka Tink nie zastępuje testu całego backendu.

Lokalne eksporty nazywaj `*.local.postman_environment.json`. Katalog `results/`
i takie eksporty są ignorowane przez Git. Nie zapisuj odpowiedzi z sekretami jako
przykładów w kolekcji. Nie udostępniaj wartości tokenów, kodów ani Link URL-i.
