using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Profile;

public sealed class UpdateUserProfileRequest : IValidatableObject
{
	private string _login = string.Empty;
	private string _name = string.Empty;
	private string _surname = string.Empty;
	private string? _phone;
	private string? _icon;

	[Required, StringLength(100, MinimumLength = 3)]
	public string Login
	{
		get => _login;
		init => _login = value?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	[Required, StringLength(100)]
	public string Name
	{
		get => _name;
		init => _name = value?.Trim() ?? string.Empty;
	}

	[Required, StringLength(100)]
	public string Surname
	{
		get => _surname;
		init => _surname = value?.Trim() ?? string.Empty;
	}

	[Phone, StringLength(30)]
	public string? Phone
	{
		get => _phone;
		init => _phone = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	public required DateOnly BirthDate { get; init; }

	[StringLength(2048)]
	public string? Icon
	{
		get => _icon;
		init => _icon = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (BirthDate == DateOnly.MinValue || BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
		{
			yield return new ValidationResult("Birth date must be a valid date that is not in the future.", [nameof(BirthDate)]);
		}
	}
}
