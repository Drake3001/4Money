using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
namespace ZPI4M.Grouping.DTOs.Groups;

public sealed class GroupFormRequest
{
	[Required] public string Name { get; init; } = null!;
	public string? CurrencyCode { get; init; }
	public string? Description { get; init; }
	public string? Icon { get; init; }
	public IFormFile? IconFile { get; init; }
	public bool RemoveIcon { get; init; }
}
