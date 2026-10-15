using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public partial class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BankAccount> BankAccounts { get; set; }

    public virtual DbSet<BankTransaction> BankTransactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        BankingConfiguration.Configure(modelBuilder);
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
