using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Tink.Configuration;

namespace ZPI4M.Banking.Tink.Link;

public sealed class TinkLinkUrlBuilder(IOptions<TinkOptions> options)
{
	public string Build(string authorizationCode, string? state = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
		var settings = options.Value;
		return QueryHelpers.AddQueryString(settings.LinkUrl, new Dictionary<string, string?>
		{
			["state"] = state,
			["client_id"] = settings.ClientId,
			["authorization_code"] = authorizationCode,
			["redirect_uri"] = settings.RedirectUri,
			["market"] = settings.Market,
			["locale"] = settings.Locale,
			["refreshable_items"] = string.Join(',', settings.RefreshableItems)
		});
	}
}
