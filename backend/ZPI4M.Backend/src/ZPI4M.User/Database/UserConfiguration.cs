using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using ZPI4M.User.Models;

namespace ZPI4M.User.Database
{
    internal class UserConfiguration
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
                    .HasColumnType("timestamp without time zone")
                    .HasColumnName("created_at");
                entity.Property(e => e.Email).HasColumnName("email");
                entity.Property(e => e.Icon).HasColumnName("icon");
                entity.Property(e => e.IsActive)
                    .HasDefaultValue(true)
                    .HasColumnName("is_active");
                entity.Property(e => e.LastLoginTime)
                    .HasColumnType("timestamp without time zone")
                    .HasColumnName("last_login_time");
                entity.Property(e => e.Login).HasColumnName("login");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
                entity.Property(e => e.Phone).HasColumnName("phone");
                entity.Property(e => e.RefreshToken).HasColumnName("refresh_token");
                entity.Property(e => e.RefreshTokenExpiryTime)
                    .HasColumnType("timestamp without time zone")
                    .HasColumnName("refresh_token_expiry_time");
                entity.Property(e => e.Surname).HasColumnName("surname");
                entity.Property(e => e.TinkUserId).HasColumnName("tink_user_id");
            });
        }
    }
}
