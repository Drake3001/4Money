using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Validation;

public sealed class SettlementAmountAttribute : ValidationAttribute
{
	public override bool IsValid(object? value) => value is null || value is decimal amount && amount > 0m && amount <= 9999999999999999.99m && decimal.Round(amount, 2) == amount;
}
