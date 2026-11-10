using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Registration;

public sealed class GoogleRegistrationRequest : IValidatableObject
{
	private string _login = string.Empty;
	private string _name = string.Empty;
	private string _surname = string.Empty;

	[Required, StringLength(16384)]
	public string IdToken { get; init; } = string.Empty;

	[Required, StringLength(100, MinimumLength = 3)]
	[RegularExpression(@"[^@]*", ErrorMessage = "Login must not contain '@'.")]
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

	public required DateOnly BirthDate { get; init; }

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (BirthDate == DateOnly.MinValue || BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
			yield return new ValidationResult("Birth date must be a valid date that is not in the future.", [nameof(BirthDate)]);
	}
}
