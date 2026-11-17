using Xunit;
using ZPI4M.Grouping.Services.Invitations;
public sealed class InvitationTokenTests
{
	[Fact]
	public void Tokens_are_random_url_safe_and_only_hash_is_persistent()
	{
		var tokens = Enumerable.Range(0, 100).Select(_ => GroupInvitationService.GenerateToken()).ToArray();
		Assert.Equal(100, tokens.Distinct().Count());
		foreach (var token in tokens)
		{
			Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
			Assert.Matches("^[A-F0-9]{64}$", GroupInvitationService.Hash(token));
			Assert.NotEqual(token, GroupInvitationService.Hash(token));
		}
	}
	[Fact]
	public async Task Malformed_tokens_are_rejected_without_database_access()
	{
		var service = new GroupInvitationService(null!, null!, null!, null!, null!, TimeProvider.System, new ValidationOnlyTransactions(), null!, null!, null!);
		Assert.Equal(400, (await service.AcceptAsync(Guid.NewGuid(), "bad")).Status);
		Assert.Equal(400, (await service.PreviewAsync("bad")).Status);
	}
}
