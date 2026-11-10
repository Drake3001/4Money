using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ZPI4M.User.DTOs.Profile;

public class UpdateUserProfileRequest : IValidatableObject
{
	private string _login = string.Empty;
	private string _name = string.Empty;
	private string _surname = string.Empty;
	private string? _phone;
	private string? _icon;

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

	public bool RemoveIcon { get; init; }

	[JsonIgnore, BindNever, ValidateNever]
	public Stream? IconContent { get; init; }

	[JsonIgnore, BindNever, ValidateNever]
	public string? IconContentType { get; init; }

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if ((RemoveIcon ? 1 : 0) + (Icon is not null ? 1 : 0) + (IconContent is not null ? 1 : 0) > 1)
		{
			yield return new ValidationResult("Choose a preset, upload an image or remove the image; only one option is allowed.", [nameof(Icon), nameof(RemoveIcon)]);
		}

		if (BirthDate == DateOnly.MinValue || BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
		{
			yield return new ValidationResult("Birth date must be a valid date that is not in the future.", [nameof(BirthDate)]);
		}
	}
}
