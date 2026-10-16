using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Receipt.Models;

namespace ZPI4M.Receipt.Database;

public partial class ReceiptDbContext : DbContext
{
	public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Models.Receipt> Receipts { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		ReceiptConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
