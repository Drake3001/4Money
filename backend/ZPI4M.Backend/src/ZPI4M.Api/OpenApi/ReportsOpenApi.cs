using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace ZPI4M.Api.OpenApi;

internal static class ReportsOpenApi
{
	public static void Configure(OpenApiOperation operation, string? routeName)
	{
		var details = routeName switch
		{
			"getCategoryBreakdown" => "Totals sorted descending. Up to eight categories, or the largest eight plus Other. categoryId=null identifies Other independently of its name.",
			"getExpenseTotalsByPeriodAndCategory" => "Calendar months touched (including partial edges): up to 24 => Month, 25-48 => Quarter, above 48 => Year. No granularity input. The same top eight categories across the entire range plus Other per period; missing periods are zero-filled.",
			"getExpenseTotalsByDayOfWeek" => "Average daily amounts Monday through Sunday: divide sums by every occurrence of each weekday, including dates without expenses. A weekday absent from the range has average zero.",
			"getDailyTotals" => "Every calendar date in the inclusive range, chronologically, zero-filled across months and years.",
			_ => null
		};
		if (details is null) return;
		operation.Description = details + " Requires authentication. Personal scope uses the actor's shares; group scope uses accessible group purchases. " +
			"Actor comes from authentication, never a query parameter. Personal forbids groupId; group requires an accessible groupId. " +
			"Repeat categoryIds=UUID for selections. Omit categoryIds for an empty selection and empty result, never all categories. " +
			"Standard ASP.NET Core Guid collection binding rejects empty values (including categoryIds=), mixed empty/UUID values and invalid UUIDs with 400.";
		operation.Parameters =
		[
			new OpenApiParameter
			{
				Name = "dateFrom", In = ParameterLocation.Query, Required = true,
				Description = "Inclusive UTC calendar start date in YYYY-MM-DD format, on or after 1970-01-01; must not exceed dateTo. Earlier dates return 400.",
				Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
				Example = JsonValue.Create("2026-11-01")
			},
			new OpenApiParameter
			{
				Name = "dateTo", In = ParameterLocation.Query, Required = true,
				Description = "Inclusive UTC calendar end date in YYYY-MM-DD format, on or after 1970-01-01. Earlier dates return 400. No maximum range length or business upper date limit; .NET DateOnly limits apply.",
				Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
				Example = JsonValue.Create("2026-11-30")
			},
			new OpenApiParameter
			{
				Name = "scope", In = ParameterLocation.Query, Required = true,
				Description = "Exactly personal or group (lowercase). Personal forbids groupId; group requires groupId.",
				Schema = new OpenApiSchema
				{
					Type = JsonSchemaType.String,
					Enum = [JsonValue.Create("personal")!, JsonValue.Create("group")!]
				},
				Example = JsonValue.Create("personal")
			},
			new OpenApiParameter
			{
				Name = "groupId", In = ParameterLocation.Query,
				Description = "Nonempty group UUID, required only for group scope. Group access is verified through existing Expense/Grouping mechanisms.",
				Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
				Example = JsonValue.Create("20000000-0000-0000-0000-000000000001")
			},
			new OpenApiParameter
			{
				Name = "categoryIds", In = ParameterLocation.Query, Required = false,
				Style = ParameterStyle.Form, Explode = true,
				Description = "Repeat categoryIds=UUID for each category. Omit for an empty selection; never defaults to all. Standard ASP.NET Core Guid collection binding applies: empty, whitespace-only, malformed or mixed empty/UUID values return 400. All-zero UUIDs are rejected by Reports validation.",
				Schema = new OpenApiSchema
				{
					Type = JsonSchemaType.Array,
					Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
				},
				Examples = new Dictionary<string, IOpenApiExample>
				{
					["one"] = new OpenApiExample { Summary = "One selected category", Value = JsonNode.Parse("[\"30000000-0000-0000-0000-000000000001\"]") },
					["multiple"] = new OpenApiExample { Summary = "Repeated query parameter", Value = JsonNode.Parse("[\"30000000-0000-0000-0000-000000000001\",\"30000000-0000-0000-0000-000000000010\"]") },
					["empty"] = new OpenApiExample { Summary = "Empty selection: omit categoryIds from the query", Value = JsonNode.Parse("[]") }
				}
			}
		];
	}
}
