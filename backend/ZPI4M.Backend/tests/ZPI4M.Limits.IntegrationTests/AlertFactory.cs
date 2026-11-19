using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Repositories.LimitAlertRepository;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Limits.IntegrationTests;

internal static class AlertFactory
{
	public static LimitAlertService Create(LimitsDbContext db, ExpenseDbContext expense, GroupingDbContext grouping, NotificationsDbContext inbox, IGroupingTransactionCoordinator tx, TimeProvider clock, decimal threshold = 80, INotificationService? notifications = null, IExpenseAggregationService? reader = null) => new(db, expense, new LimitAlertRepository(db), new(reader ?? new ExpenseAggregationService(new ExpenseRepository(expense), new GroupExpenseMetadataLookup(new GroupRepository(grouping))), reader as IExpenseSystemAggregationService ?? new ExpenseSystemAggregationService(new ExpenseRepository(expense), tx)), new(clock), new GroupExpenseMetadataLookup(new GroupRepository(grouping)), new GroupMemberLookup(new GroupMemberRepository(grouping)), new CategoryLookupService(new CategoryRepository(expense)), notifications ?? new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new ActiveUsers(db)), tx, Options.Create(new LimitsAlertsOptions { WarningThresholdPercent = threshold }), clock);
	// Exercise the real inbox service and active-account filter on the shared connection.
	private sealed class ActiveUsers(LimitsDbContext db) : IUserLookupService
	{
		public async Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => await db.Database.SqlQuery<Guid>($"SELECT _id AS \"Value\" FROM \"user\".\"user\" WHERE is_active AND _id = ANY({ids.ToArray()})").ToArrayAsync(cancellationToken);
	}
}
