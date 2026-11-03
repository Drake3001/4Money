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

				entity.ToTable("notification", "notifications", table =>
				{
					table.HasCheckConstraint("c_notification_valid_type", "type IN (0, 1, 2, 3, 4, 5)");
				});

				entity.HasIndex(e => new { e.CreatedAt, e.Id }, "ix_notification_created_at_id")
					.IsDescending(true, true);

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
				entity.Property(e => e.FkExpenseId).HasColumnName("fk_expense_id");
				entity.HasIndex(e => e.FkExpenseId, "ix_notification_fk_expense_id");
				entity.Property(e => e.CreatedAt)
					.HasDefaultValueSql("CURRENT_TIMESTAMP")
					.HasColumnType("timestamp with time zone")
					.HasColumnName("created_at");
				entity.Property(e => e.Text).HasColumnName("text");
				entity.Property(e => e.Title).IsRequired().HasColumnName("title");
				entity.Property(e => e.Type).HasConversion<int>()
					.HasDefaultValue(NotificationType.General).HasColumnName("type");
			});

			modelBuilder.Entity<NotificationRecipient>(entity =>
			{
				entity.HasKey(e => new { e.FkNotificationId, e.FkUserId })
					.HasName("notification_recipient_pkey");
				entity.ToTable("notification_recipient", "notifications");
				entity.Property(e => e.FkNotificationId).HasColumnName("fk_notification_id");
				entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
				entity.Property(e => e.ReadAt)
					.HasColumnType("timestamp with time zone").HasColumnName("read_at");

				entity.HasIndex(e => new { e.FkUserId, e.FkNotificationId }, "ix_notification_recipient_user");
				entity.HasIndex(e => new { e.FkUserId, e.FkNotificationId }, "ix_notification_recipient_unread")
					.HasFilter("read_at IS NULL");

				entity.HasOne(e => e.FkNotification).WithMany(e => e.NotificationRecipients)
					.HasForeignKey(e => e.FkNotificationId)
					.OnDelete(DeleteBehavior.Cascade)
					.HasConstraintName("c_notification_recipient_fk_notification_id");
				entity.HasOne<ZPI4M.User.Models.User>().WithMany()
					.HasForeignKey(e => e.FkUserId)
					.OnDelete(DeleteBehavior.Restrict)
					.HasConstraintName("c_notification_recipient_fk_user_id");
			});
		}
	}
}
