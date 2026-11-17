using Xunit;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;

public sealed class GroupMemberDisplayNameTests
{
	[Fact]
	public async Task List_reads_names_for_current_and_former_members_in_one_batch()
	{
		var members = new Members();
		var names = new Names();
		var service = new GroupMemberService(null!, members, null!, null!, names);
		var result = await service.ListAsync(Guid.NewGuid(), Guid.NewGuid(), new() { Membership = "all" });
		Assert.Equal(200, result.Status);
		Assert.Equal(new[] { "Anna Testowa", "Bartek Testowy" }, result.Value!.Items.Select(m => m.DisplayName));
		Assert.Equal(1, names.Calls);
		Assert.Equal(members.Rows.Select(m => m.FkUserId), names.Requested);
		Assert.False(result.Value.Items.Last().IsMember);
	}

	[Fact]
	public async Task Details_include_the_authorized_members_name()
	{
		var members = new Members();
		var names = new Names();
		var service = new GroupMemberService(null!, members, null!, null!, names);
		var result = await service.GetAsync(Guid.NewGuid(), Guid.NewGuid(), members.Rows[1].FkUserId);
		Assert.Equal("Bartek Testowy", result.Value!.DisplayName);
		Assert.Equal(new[] { members.Rows[1].FkUserId }, names.Requested);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Unauthorized_read_does_not_look_up_names(bool list)
	{
		var members = new Members { Authorized = false };
		var names = new Names();
		var service = new GroupMemberService(null!, members, null!, null!, names);
		var status = list
			? (await service.ListAsync(Guid.NewGuid(), Guid.NewGuid(), new())).Status
			: (await service.GetAsync(Guid.NewGuid(), Guid.NewGuid(), members.Rows[0].FkUserId)).Status;
		Assert.Equal(404, status);
		Assert.Equal(0, names.Calls);
	}

	[Fact]
	public async Task Missing_member_does_not_look_up_an_unrelated_users_name()
	{
		var names = new Names();
		var service = new GroupMemberService(null!, new Members(), null!, null!, names);
		Assert.Equal(404, (await service.GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).Status);
		Assert.Equal(0, names.Calls);
	}

	private sealed class Names : IUserDisplayNameLookup
	{
		public int Calls;
		public Guid[] Requested = [];
		public Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
		{
			Calls++;
			Requested = ids.ToArray();
			return Task.FromResult<IReadOnlyDictionary<Guid, string>>(Requested.ToDictionary(id => id,
				id => id == Members.First ? "Anna Testowa" : "Bartek Testowy"));
		}
	}

	private sealed class Members : IGroupMemberRepository
	{
		public static readonly Guid First = Guid.Parse("10000000-0000-0000-0000-000000000001");
		public bool Authorized = true;
		public GroupMember[] Rows = [new() { FkUserId = First }, new() { FkUserId = Guid.Parse("10000000-0000-0000-0000-000000000002"), IsMember = false }];
		public Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Authorized);
		public Task<GroupMember?> GetReadAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(m => m.FkUserId == userId));
		public Task<PagedResult<GroupMember>> GetPageAsync(Guid groupId, GroupMemberQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<GroupMember>(Rows, Rows.Length, query.PageNumber, query.PageSize));
		public Task<IReadOnlySet<Guid>> GetActiveUsersAmongAsync(Guid groupId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) => throw new NotSupportedException();
		public Task<Dictionary<Guid, GroupMember>> GetManyAsync(Guid groupId, IReadOnlyCollection<Guid> users, CancellationToken ct = default) => throw new NotSupportedException();
		public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(Guid groupId, Guid? excludedUserId = null, CancellationToken ct = default) => throw new NotSupportedException();
		public void Add(GroupMember entity) => throw new NotSupportedException();
		public void Remove(GroupMember entity) => throw new NotSupportedException();
		public Task<GroupMember?> GetAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<int> CountAsync(Guid groupId, bool? isAdmin = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
