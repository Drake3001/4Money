using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Models;

namespace ZPI4M.Expense.Database;

public partial class ExpenseDbContext : DbContext
{
	public ExpenseDbContext(DbContextOptions<ExpenseDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Category> Categories { get; set; }

	public virtual DbSet<Models.Expense> Expenses { get; set; }

	public virtual DbSet<ExpenseItem> ExpenseItems { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		ExpenseConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
