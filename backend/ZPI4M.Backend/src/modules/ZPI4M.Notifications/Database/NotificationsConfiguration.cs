using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.Database
{
	public static class NotificationsConfiguration
	{
		public static void Configure(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Notification>(entity =>
			{
				entity.HasKey(e => e.Id).HasName("notification_pkey");

				entity.ToTable("notification", "notifications");

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
				entity.Property(e => e.SentAt)
					.HasDefaultValueSql("CURRENT_TIMESTAMP")
					.HasColumnType("timestamp with time zone")
					.HasColumnName("sent_at");
				entity.Property(e => e.Text).HasColumnName("text");
			});
		}
	}
}
