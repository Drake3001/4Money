using System.Reflection;
using Xunit;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Grouping.DTOs.Settlements;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.Shared.Pagination;
public sealed class SettlementPrivacyTests
{
	[Theory]
	[InlineData(null, true)]
	[InlineData(BankingErrorCode.NotFound, false)]
	[InlineData(BankingErrorCode.Forbidden, false)]
	[InlineData(BankingErrorCode.TemporarilyUnavailable, false)]
	public async Task Visibility_is_checked_for_actor_and_cached_only_within_response(BankingErrorCode? error, bool visible)
	{
		var actor = Guid.NewGuid(); var group = Guid.NewGuid(); var id = Guid.NewGuid(); var calls = 0;
		var bank = Proxy<IBankingReadService>((method, args) =>
		{
			Assert.Equal(nameof(IBankingReadService.GetTransactionAsync), method.Name);
			Assert.Equal(actor, args![0]); Assert.Equal(id, args[1]); calls++;
			return Task.FromResult(error.HasValue ? BankingResult<BankTransactionDetails>.Failure(error.Value) : BankingResult<BankTransactionDetails>.Success(new(id, Guid.NewGuid(), -5, "PLN", DateOnly.FromDateTime(DateTime.UtcNow), null, "Booked")));
		});
		var members = Proxy<IGroupMemberRepository>((_, _) => Task.FromResult(true));
		var rows = new[] { new GroupSettlement { Id = Guid.NewGuid(), GroupId = group, BankTransactionId = id, SettledAt = DateTime.UtcNow }, new GroupSettlement { Id = Guid.NewGuid(), GroupId = group, BankTransactionId = id, SettledAt = DateTime.UtcNow } };
		var repo = Proxy<IGroupSettlementRepository>((method, _) => method.Name == "GetAsync" ? Task.FromResult<GroupSettlement?>(rows[0]) : Task.FromResult(new PagedResult<GroupSettlement>(rows, 2, 1, 20)));
		var service = new GroupSettlementService(null!, null!, members, repo, null!, null!, TimeProvider.System, bank, null!);
		var page = await service.ListAsync(actor, group, new());
		Assert.All(page.Value!.Items, s => Assert.Equal(visible ? (Guid?)id : null, s.BankTransactionId)); Assert.Equal(1, calls);
		await service.GetAsync(actor, group, rows[0].Id); Assert.Equal(2, calls);
	}
	[Fact]
	public async Task Missing_access_or_link_never_calls_banking()
	{
		var bank = Proxy<IBankingReadService>((_, _) => throw new Exception("Unexpected banking lookup"));
		var row = new GroupSettlement { Id = Guid.NewGuid(), SettledAt = DateTime.UtcNow };
		var repo = Proxy<IGroupSettlementRepository>((_, _) => Task.FromResult<GroupSettlement?>(row));
		var denied = new GroupSettlementService(null!, null!, Proxy<IGroupMemberRepository>((_, _) => Task.FromResult(false)), repo, null!, null!, TimeProvider.System, bank, null!);
		Assert.Equal(404, (await denied.GetAsync(Guid.NewGuid(), Guid.NewGuid(), row.Id)).Status);
		var allowed = new GroupSettlementService(null!, null!, Proxy<IGroupMemberRepository>((_, _) => Task.FromResult(true)), repo, null!, null!, TimeProvider.System, bank, null!);
		Assert.Null((await allowed.GetAsync(Guid.NewGuid(), Guid.NewGuid(), row.Id)).Value!.BankTransactionId);
	}

	[Theory]
	[InlineData("bankTransactionId")]
	[InlineData("settledAt")]
	[InlineData("amount")]
	public async Task Validation_reports_the_actual_invalid_field(string field)
	{
		var service = new GroupSettlementService(null!, null!, null!, null!, null!, new ValidationOnlyTransactions(), TimeProvider.System, null!, null!);
		var request = new CreateGroupSettlementRequest
		{
			FromUserId = Guid.NewGuid(),
			ToUserId = Guid.NewGuid(),
			Amount = field == "amount" ? -1 : 5,
			Method = SettlementMethod.Cash,
			SettledAt = field == "settledAt" ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow.AddMinutes(-1),
			BankTransactionId = field == "bankTransactionId" ? Guid.NewGuid() : null
		};
		var result = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), request);
		Assert.Equal(400, result.Status); Assert.True(result.Errors!.ContainsKey(field));
		if (field != "bankTransactionId") Assert.DoesNotContain("bankTransactionId", result.Errors.Keys);
	}
	private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class
	{
		var value = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)value).Action = action; return value;
	}
	public class TestProxy : DispatchProxy
	{
		public Func<MethodInfo, object?[]?, object?> Action = null!;
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Action(targetMethod!, args);
	}
}
