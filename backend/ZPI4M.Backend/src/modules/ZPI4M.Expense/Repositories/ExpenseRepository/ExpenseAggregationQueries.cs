namespace ZPI4M.Expense.Repositories.ExpenseRepository;

internal static class ExpenseAggregationQueries
{
	internal static string GetTotalsQuery(ExpenseAggregationType aggregation)
	{
		// Only fixed expressions are interpolated. All caller-supplied values are SQL parameters.
		var period = aggregation switch
		{
			ExpenseAggregationType.Month => "date_trunc('month', day)::date",
			ExpenseAggregationType.Quarter => "date_trunc('quarter', day)::date",
			ExpenseAggregationType.Year => "date_trunc('year', day)::date",
			ExpenseAggregationType.Day => "day",
			ExpenseAggregationType.Category or ExpenseAggregationType.DayOfWeek => "DATE '0001-01-01'",
			_ => throw new ArgumentOutOfRangeException(nameof(aggregation))
		};
		var category = aggregation is ExpenseAggregationType.Day or ExpenseAggregationType.DayOfWeek
			? "'00000000-0000-0000-0000-000000000000'::uuid" : "category_id";
		var weekday = aggregation == ExpenseAggregationType.DayOfWeek ? "extract(dow FROM day)::int" : "0";
		return AllocatedAmounts + $"""

			SELECT {period} AS "Period", {category} AS "CategoryId", {weekday} AS "Weekday",
				sum(total_amount) AS "TotalAmount"
			FROM selected
			GROUP BY 1, 2, 3
			ORDER BY 1, 2, 3
			""";
	}

	// PostgreSQL numeric round matches CalculateItemTotal's AwayFromZero.
	// Both largest-remainder allocations preserve the agreed per-expense share and category rounding rules.
	// Keep all categories until cents have been distributed: selection must not change rounding.
	internal const string AllocatedAmounts = """
		WITH eligible AS (
			SELECT e._id, e.amount,
				CASE WHEN e.transaction_time = '-infinity'::timestamptz THEN DATE '0001-01-01'
					WHEN e.transaction_time = 'infinity'::timestamptz THEN DATE '9999-12-31'
					ELSE (e.transaction_time AT TIME ZONE 'UTC')::date END AS day
			FROM expense.expense e
			WHERE (e.transaction_time >= (@dateFrom::date::timestamp AT TIME ZONE 'UTC')
					OR (@dateFrom::date = DATE '0001-01-01' AND e.transaction_time = '-infinity'::timestamptz))
				AND (e.transaction_time < ((@dateTo::date + 1)::timestamp AT TIME ZONE 'UTC')
					OR (@dateTo::date = DATE '9999-12-31' AND e.transaction_time = 'infinity'::timestamptz))
				AND (e.fk_group_id = ANY(@active) OR e.fk_payer_id = @actor
					OR EXISTS (SELECT 1 FROM expense.expense_share s
						WHERE s.fk_expense_id = e._id AND s.fk_user_id = @actor))
				AND ((@personal AND EXISTS (SELECT 1 FROM expense.expense_share s
						WHERE s.fk_expense_id = e._id AND s.fk_user_id = @actor))
					OR (NOT @personal AND e.fk_group_id = @groupId))
		), share_parts AS (
			SELECT e._id, e.amount, s.fk_user_id, s.percentage,
				floor(e.amount * s.percentage) AS cents,
				row_number() OVER (PARTITION BY e._id
					ORDER BY e.amount * s.percentage - floor(e.amount * s.percentage) DESC, s.fk_user_id) AS priority,
				sum(floor(e.amount * s.percentage)) OVER (PARTITION BY e._id) AS allocated
			FROM eligible e JOIN expense.expense_share s ON s.fk_expense_id = e._id
			WHERE @personal
		), actor_share AS (
			SELECT _id, percentage,
				cents + CASE WHEN priority <= amount * 100 - allocated THEN 1 ELSE 0 END AS target_cents
			FROM share_parts WHERE fk_user_id = @actor
		), category_amounts AS (
			SELECT e._id, e.day, i.fk_category_id AS category_id,
				sum(round(i.unit_price * i.quantity, 2) + i.discount) AS amount
			FROM eligible e JOIN expense.expense_item i ON i.fk_expense_id = e._id
			WHERE @personal OR @allCategories OR i.fk_category_id = ANY(@categories)
			GROUP BY e._id, e.day, i.fk_category_id
		), category_parts AS (
			SELECT c.*, s.target_cents,
				floor(c.amount * s.percentage) AS cents,
				row_number() OVER (PARTITION BY c._id
					ORDER BY c.amount * s.percentage - floor(c.amount * s.percentage) DESC, c.category_id) AS priority,
				sum(floor(c.amount * s.percentage)) OVER (PARTITION BY c._id) AS allocated
			FROM category_amounts c JOIN actor_share s ON s._id = c._id
		), amounts AS (
			SELECT day, category_id,
				(cents + CASE WHEN priority <= target_cents - allocated THEN 1 ELSE 0 END) / 100 AS total_amount
			FROM category_parts WHERE @personal
			UNION ALL
			SELECT day, category_id, amount FROM category_amounts WHERE NOT @personal
		), selected AS (
			SELECT * FROM amounts WHERE @allCategories OR category_id = ANY(@categories)
		)
		""";
}
