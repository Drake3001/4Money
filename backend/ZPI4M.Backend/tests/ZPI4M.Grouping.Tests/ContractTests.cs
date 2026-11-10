using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZPI4M.Grouping.ApiControllers;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.CurrentUser;

public class ContractTests
{
	[Theory]
	[InlineData(true, 501)]
	[InlineData(false, 401)]
	public async Task RoutesExposeStubs(bool authenticated, int status)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers().AddApplicationPart(typeof(GroupsController).Assembly);
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Groups.IGroupService, ContractGroupService>();
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Groups.IGroupIconService, ContractIconService>();
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Invitations.IGroupInvitationService, ContractInvitationService>();
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Members.IGroupMemberService, ContractMemberService>();
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Settlements.IGroupSettlementService, ContractSettlementService>();
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Suggestions.IGroupSettlementSuggestionService, ContractSuggestionService>();
		builder.Services.AddAuthorization();
		builder.Services.AddOpenApi();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		await using var app = builder.Build();
		app.Use(async (context, next) =>
		{
			context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", authenticated ? Guid.NewGuid().ToString() : "invalid") }, "test"));
			await next(context);
		});
		app.UseAuthorization();
		app.MapControllers();
		app.MapOpenApi();
		await app.StartAsync();
		using var client = app.GetTestClient();
		var specification = await client.GetStringAsync("/openapi/v1.json");
		Assert.Contains("GroupInvitationResponse", specification);
		Assert.Contains("pageNumber", specification);
		Assert.Contains("descending", specification, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("tokenHash", specification);
		Assert.Contains("GroupSettlementResponse", specification);
		var group = "/api/groups/" + Guid.NewGuid();
		foreach (var (method, route, body) in new[] {
			("POST", "/api/groups", "{\"name\":\"Test\",\"currencyCode\":\"PLN\"}"),
			("GET", group + "/settlements", ""),
 ("GET", group + "/settlements/suggestions", ""),
("GET", group + "/settlements/" + Guid.NewGuid(), ""),
("POST", group + "/settlements", "{\"fromUserId\":\"10000000-0000-0000-0000-000000000001\",\"toUserId\":\"10000000-0000-0000-0000-000000000002\",\"amount\":10.50,\"method\":1,\"settledAt\":\"2030-01-01T00:00:00Z\"}"),
("PUT", group + "/settlements/" + Guid.NewGuid(), "{\"fromUserId\":\"10000000-0000-0000-0000-000000000001\",\"toUserId\":\"10000000-0000-0000-0000-000000000002\",\"amount\":10.50,\"method\":1,\"settledAt\":\"2030-01-01T00:00:00Z\"}"),
("DELETE", group + "/settlements/" + Guid.NewGuid(), ""),
("POST", "/api/group-invitations/accept", "{\"token\":\"synthetic-token\"}"),
("PUT", group + "/administrator", "{\"userId\":\"10000000-0000-0000-0000-000000000002\"}"),
("GET", "/api/groups", ""), ("GET", group, ""),
			("PUT", group, "{\"name\":\"Test\"}"),
			("GET", group + "/members", ""), ("GET", group + "/members/" + Guid.NewGuid(), ""),
			("POST", group + "/leave", ""), ("DELETE", group + "/members/" + Guid.NewGuid(), ""),
			("GET", group + "/invitations", ""),
			("POST", group + "/invitations", "{\"maxUses\":1,\"expiresAt\":\"2030-01-01T00:00:00Z\"}"),
			("DELETE", group + "/invitations/" + Guid.NewGuid(), "") })
		{
			using var request = new HttpRequestMessage(new HttpMethod(method), route);
			if (body.Length > 0) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
			using var response = await client.SendAsync(request);
			var implemented = route == "/api/groups" || route == group || route == group + "/administrator" || route.Contains("/invitations") || route == "/api/group-invitations/accept" || route.Contains("/settlements") || route.Contains("/members") || route.EndsWith("/leave");
			Assert.Equal(authenticated && implemented ? (method == "DELETE" || route.EndsWith("/leave") ? 204 : method == "POST" && route != "/api/group-invitations/accept" ? 201 : 200) : status, (int)response.StatusCode);
		}
		if (authenticated)
		{
			foreach (var query in new[] { "pageSize=101", "pageNumber=0", "pageNumber=2147483647&pageSize=100", "descending=bad" })
				foreach (var route in new[] { "/api/groups", group + "/members", group + "/invitations", group + "/settlements" })
					Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(route + "?" + query)).StatusCode);
			Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(group + "/members?membership=bad")).StatusCode);
			Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(group + "/members?descending=true")).StatusCode);
		}
	}
}

public sealed class ContractGroupService : ZPI4M.Grouping.Services.Groups.IGroupService
{
	private static ZPI4M.Grouping.DTOs.Groups.GroupResponse Value => new(Guid.NewGuid(), "Test", null, Guid.NewGuid(), "PLN", true);
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>> CreateAsync(Guid u, ZPI4M.Grouping.DTOs.Groups.CreateGroupRequest r, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>(201, Value));
	public Task<GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>>> GetPageAsync(Guid u, ZPI4M.Grouping.DTOs.Groups.GroupListRequest q, CancellationToken ct = default) => Task.FromResult(new GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>>(200, new([], 0, q.PageNumber, q.PageSize)));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>> GetAsync(Guid u, Guid g, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>(200, Value));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>> UpdateAsync(Guid u, Guid g, ZPI4M.Grouping.DTOs.Groups.UpdateGroupRequest r, CancellationToken ct = default) => GetAsync(u, g, ct);
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Groups.GroupResponse>> TransferAdministratorAsync(Guid u, Guid g, Guid target, CancellationToken ct = default) => GetAsync(u, g, ct);
	public Task<GroupingResult> ArchiveAsync(Guid u, Guid g, CancellationToken ct = default) => Task.FromResult(new GroupingResult(204));
}
public sealed class ContractIconService : ZPI4M.Grouping.Services.Groups.IGroupIconService
{
	public Task<IReadOnlyList<ZPI4M.Grouping.Services.Groups.GroupIconPreset>> GetPresetsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ZPI4M.Grouping.Services.Groups.GroupIconPreset>>([]);
	public Task<bool> IsPresetAsync(string key, CancellationToken ct) => Task.FromResult(false);
	public Task<string> UploadAsync(Guid id, Stream stream, string mime, CancellationToken ct) => throw new NotSupportedException();
	public Task<ZPI4M.Shared.Storage.FileUrlResult> GetUrlAsync(string key, CancellationToken ct) => throw new NotSupportedException();
	public Task CleanupAsync(string key) => Task.CompletedTask;
}

public sealed class ContractInvitationService : ZPI4M.Grouping.Services.Invitations.IGroupInvitationService
{
	private static ZPI4M.Grouping.DTOs.Invitations.GroupInvitationResponse Invitation => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, DateTimeOffset.UtcNow.AddDays(1), true);
	public Task<GroupingResult<ZPI4M.Grouping.DTOs.Invitations.CreateGroupInvitationResponse>> CreateAsync(Guid user, Guid group, ZPI4M.Grouping.DTOs.Invitations.CreateGroupInvitationRequest request, CancellationToken ct = default) => Task.FromResult(new GroupingResult<ZPI4M.Grouping.DTOs.Invitations.CreateGroupInvitationResponse>(201, new(Invitation, "synthetic")));
	public Task<GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Invitations.GroupInvitationResponse>>> ListAsync(Guid user, Guid group, ZPI4M.Grouping.DTOs.Invitations.GroupInvitationListRequest query, CancellationToken ct = default) => Task.FromResult(new GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Invitations.GroupInvitationResponse>>(200, new([], 0, query.PageNumber, query.PageSize)));
	public Task<GroupingResult> RevokeAsync(Guid user, Guid group, Guid invitation, CancellationToken ct = default) => Task.FromResult(new GroupingResult(204));
	public Task<GroupingResult<ZPI4M.Grouping.Services.Invitations.InvitationPreview>> PreviewAsync(string token, CancellationToken ct = default) => Task.FromResult(new GroupingResult<ZPI4M.Grouping.Services.Invitations.InvitationPreview>(200, new(Guid.NewGuid(), "Test", null, "PLN", null, null, DateTimeOffset.UtcNow.AddDays(1), 1)));
	public Task<GroupingResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>> AcceptAsync(Guid user, string token, CancellationToken ct = default) => Task.FromResult(new GroupingResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>(200, new(user, Guid.NewGuid(), true, DateTime.UtcNow, 0, 0, 0, 0)));
}

public sealed class ContractMemberService : ZPI4M.Grouping.Services.Members.IGroupMemberService
{
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>>> ListAsync(Guid actor, Guid group, ZPI4M.Grouping.DTOs.Members.GroupMemberListRequest query, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>>(200, new([], 0, query.PageNumber, query.PageSize)));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>> GetAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Members.GroupMemberResponse>(200, new(user, group, true, DateTime.UtcNow, 0, 0, 0, 0)));
	public Task<GroupingResult<IReadOnlySet<Guid>>> GetActiveMemberIdsAsync(Guid actor, Guid group,
		IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
		Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(200, userIds.ToHashSet()));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult> LeaveAsync(Guid actor, Guid group, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult(204));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult> RemoveAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default) => LeaveAsync(actor, group, ct);
}

public sealed class ContractSettlementService : ZPI4M.Grouping.Services.Settlements.IGroupSettlementService
{
	private static ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse Value => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, ZPI4M.Grouping.Models.SettlementMethod.Cash, DateTimeOffset.UtcNow, null);
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>> CreateAsync(Guid actor, Guid group, ZPI4M.Grouping.DTOs.Settlements.CreateGroupSettlementRequest request, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>(201, Value));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>> UpdateAsync(Guid actor, Guid group, Guid id, ZPI4M.Grouping.DTOs.Settlements.UpdateGroupSettlementRequest request, CancellationToken ct = default) => GetAsync(actor, group, id, ct);
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>> GetAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>(200, Value));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult> DeleteAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult(204));
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>>> ListAsync(Guid actor, Guid group, ZPI4M.Grouping.DTOs.Settlements.GroupSettlementListRequest query, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Shared.Pagination.PagedResult<ZPI4M.Grouping.DTOs.Settlements.GroupSettlementResponse>>(200, new([], 0, query.PageNumber, query.PageSize)));
}

public sealed class ContractSuggestionService : ZPI4M.Grouping.Services.Suggestions.IGroupSettlementSuggestionService
{
	public Task<ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Suggestions.GroupSettlementSuggestionsResponse>> GetAsync(Guid actor, Guid groupId, CancellationToken ct = default) => Task.FromResult(new ZPI4M.Grouping.Services.Results.GroupingResult<ZPI4M.Grouping.DTOs.Suggestions.GroupSettlementSuggestionsResponse>(200, new(groupId, "PLN", DateTimeOffset.UtcNow, "largestBalanceFirst", false, [], [], 0)));
}
