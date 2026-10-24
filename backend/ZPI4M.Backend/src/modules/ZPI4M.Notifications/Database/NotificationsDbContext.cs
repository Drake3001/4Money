using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.Database;

public partial class NotificationsDbContext : DbContext
{
	public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Notification> Notifications { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		NotificationsConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
