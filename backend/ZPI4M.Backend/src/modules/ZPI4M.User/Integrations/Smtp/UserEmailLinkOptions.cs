namespace ZPI4M.User.Integrations.Smtp;

public sealed class UserEmailLinkOptions
{
	public const string SectionName = "UserEmailLinks";
	public string FrontendBaseUrl { get; set; } = "";

	public static bool IsValid(UserEmailLinkOptions options) =>
		Uri.TryCreate(options.FrontendBaseUrl, UriKind.Absolute, out var uri) &&
		(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
		string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);
}
