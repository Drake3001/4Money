using Microsoft.AspNetCore.Http;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Expense.DTOs.Banking;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.DTOs.Receipts;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Services.Expenses;

/// <summary>Expense operations called by controllers; callers obtain actor from ICurrentUser.</summary>
public interface IExpenseService
{
	Task<ExpenseResult<IReadOnlyList<CategoryDetails>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
	Task<ExpenseResult<PagedResult<ExpenseHistoryEntry>>> ListAsync(Guid actor, ExpenseListRequest request, CancellationToken cancellationToken = default);
	Task<ExpenseResult<ExpenseView>> UpdateAsync(Guid actor, Guid expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken = default);
	Task<ExpenseResult<bool>> DeleteAsync(Guid actor, Guid expenseId, string version, CancellationToken cancellationToken = default);
	Task<ExpenseResult<ExpenseView>> CreateAsync(Guid actor, CreateExpenseRequest request, CancellationToken cancellationToken = default);
	Task<ExpenseResult<ExpensePreview>> PreviewAsync(Guid actor, PreviewExpenseRequest request, CancellationToken cancellationToken = default);
	Task<ExpenseResult<ExpenseView>> GetByIdAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken = default);
	Task<ExpenseResult<PagedResult<BankTransactionDetails>>> GetAvailableBankTransactionsAsync(Guid actor, AvailableBankTransactionRequest request, CancellationToken cancellationToken = default);
	Task<ExpenseResult<ReceiptResponse>> GetReceiptAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken);
	Task<ExpenseResult<ReceiptOcrResponse>> AnalyzeReceiptAsync(Guid actor, IFormFile file, CancellationToken cancellationToken);
}
