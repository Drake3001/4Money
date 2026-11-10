using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Expense.DTOs.Categories;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Expense.ApiControllers;

[Route("api/categories")]
public sealed class CategoriesController(ICurrentUser currentUser, IExpenseService expenses) : ExpenseControllerBase(currentUser)
{
	[HttpGet(Name = "listExpenseCategories")]
	[EndpointSummary("List expense categories")]
	[EndpointDescription("Returns all categories ordered by name and then ID. The category catalog is read-only.")]
	[ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
	public Task<IActionResult> ListExpenseCategories(CancellationToken cancellationToken) =>
		ExecuteAsync(_ => expenses.GetCategoriesAsync(cancellationToken), categories => Ok(categories.Select(ExpenseResponseMapper.Category).ToArray()));
}
