using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Models;

namespace ZPI4M.Grouping.Database;

public partial class GroupingDbContext : DbContext
{
	public GroupingDbContext(DbContextOptions<GroupingDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Group> Groups { get; set; }

	public virtual DbSet<GroupMember> GroupMembers { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		GroupingConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
