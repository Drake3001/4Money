using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Database;

public partial class LimitsDbContext : DbContext
{
    public LimitsDbContext(DbContextOptions<LimitsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CategoryGroupLimit> CategoryGroupLimits { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
       base.OnModelCreating(modelBuilder);
       LimitsConfiguration.Configure(modelBuilder);
       OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
