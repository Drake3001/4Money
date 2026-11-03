using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Xunit;
using ZPI4M.Grouping.DTOs.Groups;
using ZPI4M.Grouping.DTOs.Invitations;
using ZPI4M.Grouping.DTOs.Settlements;

public class SettlementValidationTests
{
	[Theory]
	[InlineData("{\"amount\":null}")]
	[InlineData("{\"amount\":0}")]
	[InlineData("{\"amount\":-1}")]
	[InlineData("{\"amount\":1.001}")]
	[InlineData("{\"amount\":10000000000000000}")]
	[InlineData("{\"method\":4}")]
	[InlineData("{\"fromUserId\":\"00000000-0000-0000-0000-000000000000\"}")]
	public void InvalidSettlementIsRejected(string json)
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var body = System.Text.Json.Nodes.JsonNode.Parse("""
			{"fromUserId":"10000000-0000-0000-0000-000000000001","toUserId":"10000000-0000-0000-0000-000000000002","amount":10.50,"method":1,"settledAt":"2030-01-01T00:00:00Z"}
			""")!.AsObject();
		foreach (var pair in System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject())
			body[pair.Key] = pair.Value?.DeepClone();
		json = body.ToJsonString();
		foreach (var type in new[] { typeof(CreateGroupSettlementRequest), typeof(UpdateGroupSettlementRequest) })
		{
			var request = JsonSerializer.Deserialize(json, type, options)!;
			Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true));
		}
	}

	[Fact]
	public void TokenAndAdministratorAreRequired()
	{
		foreach (var request in new object[] { new AcceptGroupInvitationRequest { Token = " " }, new TransferGroupAdministratorRequest { UserId = Guid.Empty } })
			Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true));
	}
}
