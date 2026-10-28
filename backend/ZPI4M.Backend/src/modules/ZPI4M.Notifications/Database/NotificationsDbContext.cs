using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Notifications.Models;
using ZPI4M.User.Database;

namespace ZPI4M.Notifications.Database;

public partial class NotificationsDbContext : DbContext
{
	public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Notification> Notifications { get; set; }

	public virtual DbSet<NotificationRecipient> NotificationRecipients { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		NotificationsConfiguration.Configure(modelBuilder);
		UserConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
