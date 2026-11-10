using System.ComponentModel.DataAnnotations;
using Xunit;
using ZPI4M.User.DTOs.AccountDeactivation;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.EmailChange;
using ZPI4M.User.DTOs.EmailConfirmation;
using ZPI4M.User.DTOs.PasswordReset;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.DTOs.Registration;

namespace ZPI4M.User.UnitTests;

public sealed class RequestValidationTests
{
	[Fact]
	public void RegistrationNormalizesIdentityWithoutChangingPassword()
	{
		var request = Registration(" Password1! ");
		Assert.Equal("test.user", request.Login);
		Assert.Equal("test@example.test", request.Email);
		Assert.Equal("Anna", request.Name);
		Assert.Equal("Testowa", request.Surname);
		Assert.Equal(" Password1! ", request.Password);
		Assert.True(IsValid(request));
		Assert.Equal("test.user", new LoginRequest { Login = " TEST.USER " }.Login);
	}

	[Theory]
	[InlineData(" TEST.USER ", "test.user")]
	[InlineData(" TEST@EXAMPLE.TEST ", "test@example.test")]
	public void LoginNormalizesIdentifierWithoutChangingPassword(string identifier, string expected)
	{
		var request = new LoginRequest { Login = identifier, Password = " Password1! " };
		Assert.Equal(expected, request.Login);
		Assert.Equal(" Password1! ", request.Password);
		Assert.True(IsValid(request));
	}

	[Fact]
	public void LoginAcceptsMaximumEmailLengthAndRejectsInvalidIdentifierLengths()
	{
		var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 56) + ".test";
		Assert.Equal(254, email.Length);
		Assert.True(IsValid(new LoginRequest { Login = email, Password = "Password1!" }));
		foreach (var identifier in new[] { "", "   ", "ab", new string('a', 255) })
			Assert.False(IsValid(new LoginRequest { Login = identifier, Password = "Password1!" }));
	}

	[Theory]
	[InlineData("short1!")]
	[InlineData("password1!")]
	[InlineData("Password!")]
	[InlineData("Password1")]
	[InlineData("")]
	public void RegistrationAndResetRejectWeakPasswords(string password)
	{
		Assert.False(IsValid(Registration(password)));
		Assert.False(IsValid(new ResetPasswordRequest { Token = new string('a', 43), NewPassword = password }));
	}

	[Theory]
	[InlineData("")]
	[InlineData("too-short")]
	[InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
	[InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")]
	public void EveryTokenEndpointRejectsMalformedTokens(string token)
	{
		object[] requests =
		[
			new ConfirmEmailRequest { Token = token },
			new ConfirmEmailChangeRequest { Token = token },
			new ConfirmAccountDeactivationRequest { Token = token },
			new ResetPasswordRequest { Token = token, NewPassword = "Password1!" },
			new RefreshTokenRequest { RefreshToken = token }
		];
		Assert.All(requests, request => Assert.False(IsValid(request)));
	}

	[Fact]
	public void RegistrationRejectsInvalidBirthDatesAndAcceptsToday()
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		Assert.False(IsValid(Registration("Password1!", DateOnly.MinValue)));
		Assert.False(IsValid(Registration("Password1!", today.AddDays(1))));
		Assert.True(IsValid(Registration("Password1!", today)));
	}

	[Fact]
	public void ProfileNormalizesOptionalFieldsAndRejectsFutureBirthDate()
	{
		var request = Profile(new DateOnly(2000, 1, 1));
		Assert.True(IsValid(request));
		Assert.Null(request.Phone);
		Assert.Null(request.Icon);
		Assert.Equal("test.user", request.Login);
		Assert.False(IsValid(Profile(DateOnly.MinValue)));
		Assert.False(IsValid(Profile(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))));
	}

	private static bool IsValid(object request) => Validator.TryValidateObject(request, new ValidationContext(request), [], true);

	private static RegisterUserRequest Registration(string password, DateOnly? birthDate = null) => new()
	{
		Login = " TEST.USER ",
		Name = " Anna ",
		Surname = " Testowa ",
		Email = " TEST@EXAMPLE.TEST ",
		Password = password,
		BirthDate = birthDate ?? new DateOnly(2000, 1, 1)
	};

	private static UpdateUserProfileRequest Profile(DateOnly birthDate) => new()
	{
		Login = " TEST.USER ",
		Name = "Anna",
		Surname = "Testowa",
		Phone = " ",
		Icon = " ",
		BirthDate = birthDate
	};
}
