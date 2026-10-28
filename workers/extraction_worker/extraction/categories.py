"""Shared, versioned product taxonomy for prompts, validation and evaluation."""

TAXONOMY_VERSION = "2"

# Stored values are lowercase; clients can capitalize labels for display.
CATEGORY_DEFINITIONS = {
    "żywność i napoje": "Zakupy spożywcze i napoje bezalkoholowe, również gotowa żywność ze sklepu.",
    "restauracje i kawiarnie": "Posiłki i napoje bezalkoholowe z gastronomii, także dostawy; wymaga kontekstu gastronomicznego.",
    "alkohol i tytoń": "Napoje alkoholowe, papierosy i produkty nikotynowe, również kupione w restauracji.",
    "ubrania i dodatki": "Odzież, obuwie, torby, biżuteria i dodatki odzieżowe.",
    "elektronika": "Sprzęt elektroniczny, przewody, ładowarki i części elektroniczne.",
    "dom i ogród": "Wyposażenie, meble, narzędzia, rośliny, artykuły kuchenne i ręczniki papierowe.",
    "chemia gospodarcza": "Detergenty i środki czystości, np. płyn do naczyń, proszek do prania, płyn do WC.",
    "kosmetyki i higiena": "Pielęgnacja i higiena osobista, np. szampon, pasta do zębów, podpaski, papier toaletowy.",
    "zdrowie": "Leki, wyroby medyczne, badania, wizyty lekarskie, dentysta i rehabilitacja.",
    "zwierzęta": "Karma, akcesoria dla zwierząt, ich pielęgnacja i weterynarz.",
    "transport i samochód": "Paliwo, części, serwis auta, parking, taxi, komunikacja miejska i przejazdy lądowe.",
    "podróże i noclegi": "Hotele, loty, wycieczki i usługi turystyczne; nie zgaduj celu zwykłego przejazdu.",
    "rozrywka i wydarzenia": "Kino, koncerty, gry, abonamenty rozrywkowe i książki rekreacyjne.",
    "sport i hobby": "Siłownia, sprzęt sportowy, zajęcia rekreacyjne i materiały hobbystyczne.",
    "edukacja": "Kursy, podręczniki, szkolenia i opłaty za naukę.",
    "rachunki i opłaty": "Media, prąd, gaz, woda, telefon, internet i opłaty administracyjne.",
    "usługi": "Fryzjer, barber, krawiec, naprawa obuwia, dorabianie kluczy; tylko usługi bez trafniejszej kategorii.",
    "inne": "Rozpoznany wydatek, dla którego żadna z pozostałych kategorii nie pasuje; nie służy do oznaczania niepewności.",
    "zniżka": "Samodzielna pozycja obniżająca wartość zakupów, np. opust za butelki lub rabat na cały paragon. Nie obejmuje zwrotu kaucji ani rabatu przypisanego już do produktu.",
}
CATEGORIES = tuple(CATEGORY_DEFINITIONS)

REVIEW_REASONS = (
    "unreadable_name", "ambiguous_category", "uncertain_name", "unsupported_name_change", "invalid_model_output",
)

CATEGORY_RULES = """Klasyfikuj każdą pozycję osobno. Konkretny cel ma pierwszeństwo
przed usługami: weterynarz -> zwierzęta, dentysta -> zdrowie, mechanik -> transport
i samochód. Nazwa sklepu sama nie wyznacza kategorii: kanapka ze stacji to żywność
i napoje, paliwo to transport i samochód. Piwo bezalkoholowe 0,0% to żywność i
napoje, zwykłe piwo to alkohol i tytoń. Kawa w paczce to żywność i napoje;
kawa podana w kawiarni to restauracje i kawiarnie. Ręcznik papierowy to dom i
ogród; papier toaletowy to kosmetyki i higiena; płyn do WC to chemia gospodarcza.
Nie wnioskuj celu prezentowego, inwestycyjnego ani podróży z samego miejsca
zakupu. Prezenty, inwestycje i nieustalone nie są kategoriami. Gdy brak kontekstu
uniemożliwia wybór, zwróć category=null, needs_review=true i
review_reason=ambiguous_category. Dla nieczytelnej nazwy zwróć name_corrected=null,
category=null, needs_review=true i review_reason=unreadable_name.
Samodzielną pozycję rabatową klasyfikuj jako zniżka. Samo słowo butelki nie
oznacza zniżki: zwrot kaucji należy do deposit_returns, a zakup butelki zachowuje
kategorię produktu. Przy niejasnej roli rabatu zwróć category=null i
review_reason=ambiguous_category z needs_review=true.
"""


def category_prompt():
    definitions = "\n".join(f"- {name}: {description}" for name, description in CATEGORY_DEFINITIONS.items())
    return f"Taksonomia v{TAXONOMY_VERSION}. Dozwolone kategorie:\n{definitions}\n\n{CATEGORY_RULES}"
