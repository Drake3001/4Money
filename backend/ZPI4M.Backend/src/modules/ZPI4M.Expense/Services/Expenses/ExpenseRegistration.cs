using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Expense.Services.Expenses;

public static class ExpenseRegistration
{
	internal const string ReceiptOcrRateLimitPolicy = "expense-receipt-ocr";

	public static IServiceCollection AddExpenseServices(this IServiceCollection services)
	{
		services.AddExpenseOcrRateLimiting();
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<IValidateOptions<ReceiptOcrProofOptions>, ReceiptOcrProofOptionsValidator>();
		services.AddOptions<ReceiptOcrProofOptions>()
			.BindConfiguration(ReceiptOcrProofOptions.SectionName)
			.ValidateOnStart();
		services.AddSingleton<ReceiptOcrProof>();
		services.AddSingleton<IExpenseRequestService, ExpenseRequestService>();
		services.AddSingleton<IExpenseCalculationService, ExpenseCalculationService>();
		services.AddScoped<IExpenseService, ExpenseService>();
		services.AddScoped<ExpenseQueryService>();
		services.AddScoped<IExpenseReadService, ExpenseReadService>();
		services.AddSingleton<ExpenseReportCalculator>();
		return services;
	}

	public static IServiceCollection AddExpenseOcrRateLimiting(this IServiceCollection services) =>
		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.AddPolicy(ReceiptOcrRateLimitPolicy, context =>
				context.RequestServices.GetRequiredService<ICurrentUser>().UserId is Guid userId && userId != Guid.Empty
					? RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = 5,
						Window = TimeSpan.FromMinutes(1),
						QueueLimit = 0
					})
					: RateLimitPartition.GetNoLimiter(Guid.Empty));
		});
}
