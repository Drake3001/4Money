using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Models;

namespace ZPI4M.User.Database
{
	public static class UserConfiguration
	{
		public static void Configure(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Models.User>(entity =>
			{
				entity.HasKey(e => e.Id).HasName("user_pkey");

				entity.ToTable("user", "user");

				entity.HasIndex(e => e.Email, "c_user_unique_email").IsUnique();

				entity.HasIndex(e => e.Login, "c_user_unique_login").IsUnique();

				entity.HasIndex(e => e.TinkUserId, "c_user_unique_tink_user_id").IsUnique();

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.BirthDate).HasColumnName("birth_date");
				entity.Property(e => e.CreatedAt)
					.HasDefaultValueSql("CURRENT_TIMESTAMP")
					.HasColumnType("timestamp with time zone")
					.HasColumnName("created_at");
				entity.Property(e => e.Email).IsRequired().HasColumnName("email");
				entity.Property(e => e.IsEmailConfirmed)
					.HasDefaultValue(false)
					.HasColumnName("is_email_confirmed");
				entity.Property(e => e.Icon).HasColumnName("icon");
				entity.Property(e => e.IsActive)
					.HasDefaultValue(true)
					.HasColumnName("is_active");
				entity.Property(e => e.LastLoginTime)
					.HasColumnType("timestamp with time zone")
					.HasColumnName("last_login_time");
				entity.Property(e => e.Login).HasColumnName("login");
				entity.Property(e => e.Name).HasColumnName("name");
				entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
				entity.Property(e => e.Phone).HasColumnName("phone");
				entity.Property(e => e.Surname).HasColumnName("surname");
				entity.Property(e => e.TinkUserId).HasColumnName("tink_user_id");
			});

			modelBuilder.Entity<UserToken>(entity =>
			{
				entity.HasKey(e => e.Id).HasName("user_token_pkey");

				entity.ToTable("user_token", "user", table =>
				{
					table.HasCheckConstraint("c_user_token_expiry_after_creation", "expires_at > created_at");
					table.HasCheckConstraint("c_user_token_valid_purpose", "purpose IN (1, 2, 3, 4, 5)");
					table.HasCheckConstraint("c_user_token_target_email",
						"(purpose IN (1, 2, 3, 5) AND target_email IS NOT NULL) OR (purpose = 4 AND target_email IS NULL)");
				});

				entity.HasIndex(e => e.TokenHash, "c_user_token_unique_token_hash").IsUnique();
				entity.HasIndex(e => new { e.FkUserId, e.Purpose }, "c_user_token_unique_user_purpose").IsUnique();

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
				entity.Property(e => e.TokenHash).IsRequired().HasColumnName("token_hash");
				entity.Property(e => e.Purpose).HasConversion<int>().HasColumnName("purpose");
				entity.Property(e => e.TargetEmail).HasColumnName("target_email");
				entity.Property(e => e.CreatedAt)
					.HasDefaultValueSql("CURRENT_TIMESTAMP")
					.HasColumnType("timestamp with time zone")
					.HasColumnName("created_at");
				entity.Property(e => e.ExpiresAt)
					.HasColumnType("timestamp with time zone")
					.HasColumnName("expires_at");

				entity.HasOne(e => e.FkUser).WithMany(e => e.UserTokens)
					.HasForeignKey(e => e.FkUserId)
					.OnDelete(DeleteBehavior.Cascade)
					.HasConstraintName("c_user_token_fk_user_id");
			});
		}
	}
}
