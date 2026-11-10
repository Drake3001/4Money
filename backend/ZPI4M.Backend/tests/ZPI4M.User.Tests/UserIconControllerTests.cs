using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using ZPI4M.Shared.Storage;
using ZPI4M.User.Services.Profile;

namespace ZPI4M.User.Tests;

public sealed partial class UserControllerTests
{
	[Theory]
	[InlineData("icon")]
	[InlineData("icon-presets")]
	public async Task IconEndpointsRequireAuthentication(string route)
	{
		await using var host = await TestHost.StartAsync();
		using var response = await host.Client.GetAsync("/api/users/profile/" + route);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Icons.Calls);
	}

	[Fact]
	public async Task IconEndpointsReturnUncacheableUrlsOrNotFound()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var missing = await host.Client.GetAsync("/api/users/profile/icon");
		Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
		host.Services.ProfileIconStorageKey = $"users/{host.UserId}/icons/synthetic";
		using var found = await host.Client.GetAsync("/api/users/profile/icon");
		Assert.Equal(HttpStatusCode.OK, found.StatusCode);
		Assert.True(found.Headers.CacheControl?.NoStore);
		Assert.Equal(host.Services.ProfileIconStorageKey, host.Icons.LastKey);
		using var presets = await host.Client.GetAsync("/api/users/profile/icon-presets");
		Assert.Equal(HttpStatusCode.OK, presets.StatusCode);
		Assert.True(presets.Headers.CacheControl?.NoStore);
	}

	[Theory]
	[InlineData(400)]
	[InlineData(413)]
	[InlineData(415)]
	[InlineData(503)]
	public async Task IconErrorsMapToProblemDetails(int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		host.Icons.ErrorStatus = status;
		using var response = await host.Client.GetAsync("/api/users/profile/icon-presets");
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task MultipartMapsFileAndOwnerAndRejectsInvalidProfile()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var form = new MultipartFormDataContent();
		foreach (var (key, value) in new[] { ("login", "test.user"), ("name", "Anna"), ("surname", "Testowa"), ("birthDate", "2000-01-01") })
			form.Add(new StringContent(value), key);
		using var image = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]);
		image.Headers.ContentType = new("image/png");
		form.Add(image, "iconFile", "avatar.png");
		using var response = await host.Client.PutAsync("/api/users/profile?userId=" + Guid.NewGuid(), form);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(host.UserId, host.Services.LastUserId);
		Assert.NotNull(host.Services.LastProfileRequest!.IconContent);
		Assert.Equal("image/png", host.Services.LastProfileRequest.IconContentType);
		using var invalid = new MultipartFormDataContent();
		using var bad = await host.Client.PutAsync("/api/users/profile", invalid);
		Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
	}

	[Fact]
	public async Task JsonCannotProvideStorageKeyOrServerStreamAndConflictingSelectionIsRejected()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.PutAsJsonAsync("/api/users/profile", new
		{
			login = "test.user",
			name = "Anna",
			surname = "Testowa",
			birthDate = "2000-01-01",
			iconStorageKey = "other-users/private",
			iconContent = "forged",
			iconContentType = "image/png"
		});
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Null(host.Services.LastProfileRequest!.IconContent);
		Assert.Null(host.Services.LastProfileRequest.IconContentType);
		using var conflict = await host.Client.PutAsJsonAsync("/api/users/profile", new
		{
			login = "test.user",
			name = "Anna",
			surname = "Testowa",
			birthDate = "2000-01-01",
			icon = "preset.png",
			removeIcon = true
		});
		Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
		Assert.True((await conflict.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("errors", out _));
	}

	[Theory]
	[InlineData("text/plain")]
	[InlineData("application/xml")]
	public async Task UnsupportedProfileContentTypeReturns415(string contentType)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var content = new StringContent("synthetic", System.Text.Encoding.UTF8, contentType);
		using var response = await host.Client.PutAsync("/api/users/profile", content);
		Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
		Assert.Equal(0, host.Services.Calls);
	}

	private sealed class FakeIcons : IUserIconService
	{
		public int Calls { get; private set; }
		public int ErrorStatus { get; set; }
		public string? LastKey { get; private set; }
		public Task<IReadOnlyList<UserIconPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
		{
			Calls++;
			if (ErrorStatus == 503) throw new HttpRequestException("synthetic storage failure");
			if (ErrorStatus != 0) throw new UserIconException(ErrorStatus);
			return Task.FromResult<IReadOnlyList<UserIconPreset>>([]);
		}
		public Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default)
		{
			Calls++;
			LastKey = key;
			return Task.FromResult(new FileUrlResult("https://storage.test/image", DateTimeOffset.UtcNow.AddMinutes(15)));
		}
		public Task<bool> IsPresetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
		public Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task CleanupAsync(string key) => throw new NotSupportedException();
	}
}
