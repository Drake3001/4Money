using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Registration;

public sealed class RegisterUserRequest : IValidatableObject
{
	private string _login = string.Empty;
	private string _name = string.Empty;
	private string _surname = string.Empty;
	private string _email = string.Empty;

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

	[Required, EmailAddress, StringLength(254)]
	public string Email
	{
		get => _email;
		init => _email = value?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	[Required, StringLength(128, MinimumLength = 8)]
	[RegularExpression(@"^(?=.*\p{Lu})(?=.*[0-9])(?=.*[\p{P}\p{S}]).+$",
		ErrorMessage = "Password must contain at least one uppercase letter, one digit and one special character.")]
	public string Password { get; init; } = string.Empty;

	public required DateOnly BirthDate { get; init; }

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (BirthDate == DateOnly.MinValue || BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
		{
			yield return new ValidationResult("Birth date must be a valid date that is not in the future.", [nameof(BirthDate)]);
		}
	}
}
