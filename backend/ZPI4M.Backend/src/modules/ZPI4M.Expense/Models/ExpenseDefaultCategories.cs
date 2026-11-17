using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public static class ExpenseDefaultCategories
{
	public static readonly Category FoodAndBeverages = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
		Name = "Żywność i napoje",
		Description = "Zakupy spożywcze i napoje bezalkoholowe, również gotowa żywność ze sklepu.",
		Icon = "shopping-cart"
	};

	public static readonly Category Restaurants = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000002"),
		Name = "Restauracje i kawiarnie",
		Description = "Posiłki i napoje bezalkoholowe z gastronomii, także dostawy; wymaga kontekstu gastronomicznego.",
		Icon = "utensils"
	};

	public static readonly Category Transport = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000003"),
		Name = "Transport i samochód",
		Description = "Paliwo, części, serwis auta, parking, taxi, komunikacja miejska i przejazdy lądowe.",
		Icon = "car"
	};

	public static readonly Category BillsAndFees = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000004"),
		Name = "Rachunki i opłaty",
		Description = "Media, prąd, gaz, woda, telefon, internet i opłaty administracyjne.",
		Icon = "receipt"
	};

	public static readonly Category Unknown = new()
	{
		Id = ExpenseSystemCategories.UnknownId,
		Name = "Nieznane",
		Description = "Automatyczne dopełnienie kwoty wydatku",
		Icon = "circle-help"
	};

	public static readonly Category AlcoholAndTobacco = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000006"),
		Name = "Alkohol i tytoń",
		Description = "Napoje alkoholowe, papierosy i produkty nikotynowe, również kupione w restauracji.",
		Icon = "wine"
	};

	public static readonly Category ClothesAndAccessories = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000007"),
		Name = "Ubrania i dodatki",
		Description = "Odzież, obuwie, torby, biżuteria i dodatki odzieżowe.",
		Icon = "shirt"
	};

	public static readonly Category Electronics = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000008"),
		Name = "Elektronika",
		Description = "Sprzęt elektroniczny, przewody, ładowarki i części elektroniczne.",
		Icon = "smartphone"
	};

	public static readonly Category HomeAndGarden = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000009"),
		Name = "Dom i ogród",
		Description = "Wyposażenie, meble, narzędzia, rośliny, artykuły kuchenne i ręczniki papierowe.",
		Icon = "home"
	};

	public static readonly Category HouseholdChemicals = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000010"),
		Name = "Chemia gospodarcza",
		Description = "Detergenty i środki czystości, np. płyn do naczyń, proszek do prania, płyn do WC.",
		Icon = "spray-can"
	};

	public static readonly Category CosmeticsAndHygiene = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000011"),
		Name = "Kosmetyki i higiena",
		Description = "Pielęgnacja i higiena osobista, np. szampon, pasta do zębów, podpaski, papier toaletowy.",
		Icon = "sparkles"
	};

	public static readonly Category Health = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000012"),
		Name = "Zdrowie",
		Description = "Leki, wyroby medyczne, badania, wizyty lekarskie, dentysta i rehabilitacja.",
		Icon = "heart-pulse"
	};

	public static readonly Category Pets = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000013"),
		Name = "Zwierzęta",
		Description = "Karma, akcesoria dla zwierząt, ich pielęgnacja i weterynarz.",
		Icon = "paw-print"
	};

	public static readonly Category Travel = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000014"),
		Name = "Podróże i noclegi",
		Description = "Hotele, loty, wycieczki i usługi turystyczne; nie zgaduj celu zwykłego przejazdu.",
		Icon = "plane"
	};

	public static readonly Category Entertainment = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000015"),
		Name = "Rozrywka i wydarzenia",
		Description = "Kino, koncerty, gry, abonamenty rozrywkowe i książki rekreacyjne.",
		Icon = "ticket"
	};

	public static readonly Category SportsAndHobbies = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000016"),
		Name = "Sport i hobby",
		Description = "Siłownia, sprzęt sportowy, zajęcia rekreacyjne i materiały hobbystyczne.",
		Icon = "dumbbell"
	};

	public static readonly Category Education = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000017"),
		Name = "Edukacja",
		Description = "Kursy, podręczniki, szkolenia i opłaty za naukę.",
		Icon = "graduation-cap"
	};

	public static readonly Category Services = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000018"),
		Name = "Usługi",
		Description = "Fryzjer, barber, krawiec, naprawa obuwia, dorabianie kluczy; tylko usługi bez trafniejszej kategorii.",
		Icon = "wrench"
	};

	public static readonly Category Other = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000019"),
		Name = "Inne",
		Description = "Rozpoznany wydatek, dla którego żadna z pozostałych kategorii nie pasuje; nie służy do oznaczania niepewności.",
		Icon = "package"
	};

	public static readonly Category Discount = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000020"),
		Name = "Zniżka",
		Description = "Samodzielna pozycja obniżająca wartość zakupów, np. opust za butelki lub rabat na cały paragon. Nie obejmuje zwrotu kaucji ani rabatu przypisanego już do produktu.",
		Icon = "badge-percent"
	};

	public static readonly Category Deposit = new()
	{
		Id = Guid.Parse("30000000-0000-0000-0000-000000000021"),
		Name = "Kaucja",
		Description = "Kaucje za opakowania i ich zwroty.",
		Icon = "recycle"
	};

	public static readonly IReadOnlyList<Category> All = new[]
	{
		FoodAndBeverages,
		Restaurants,
		Transport,
		BillsAndFees,
		Unknown,
		AlcoholAndTobacco,
		ClothesAndAccessories,
		Electronics,
		HomeAndGarden,
		HouseholdChemicals,
		CosmeticsAndHygiene,
		Health,
		Pets,
		Travel,
		Entertainment,
		SportsAndHobbies,
		Education,
		Services,
		Other,
		Discount,
		Deposit
	};
}
