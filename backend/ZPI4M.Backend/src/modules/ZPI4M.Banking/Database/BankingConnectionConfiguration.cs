using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public static partial class BankingConfiguration
{
	private static void ConfigureConnections(ModelBuilder modelBuilder)
	{
		// Use the User module's existing mapping for cross-module foreign keys.
		ZPI4M.User.Database.UserConfiguration.Configure(modelBuilder);
		var attempt = modelBuilder.Entity<BankConnectionAttempt>();
		attempt.ToTable("bank_connection_attempt", "banking");
		attempt.HasKey(x => x.Id);
		attempt.Property(x => x.Id).ValueGeneratedNever();
		attempt.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
		attempt.Property(x => x.Version).IsConcurrencyToken();
		attempt.Property(x => x.CorrelationStateHash).HasMaxLength(64);
		attempt.Property(x => x.TinkUserId).HasMaxLength(128);
		attempt.Property(x => x.FailureCode).HasMaxLength(64);
		attempt.HasIndex(x => x.CorrelationStateHash).IsUnique();
		attempt.HasIndex(x => new { x.FourMoneyUserId, x.CreatedAt });
		attempt.HasIndex(x => new { x.Status, x.ExpiresAt });
		attempt.HasOne<BankConnection>().WithMany().HasForeignKey(x => x.BankConnectionId).OnDelete(DeleteBehavior.Restrict);
		attempt.HasOne<ZPI4M.User.Models.User>().WithMany().HasForeignKey(x => x.FourMoneyUserId).OnDelete(DeleteBehavior.Restrict);

		var connection = modelBuilder.Entity<BankConnection>();
		connection.ToTable("bank_connection", "banking");
		connection.HasKey(x => x.Id);
		connection.Property(x => x.Id).ValueGeneratedNever();
		connection.Property(x => x.TinkUserId).HasMaxLength(128);
		connection.Property(x => x.CredentialsId).HasMaxLength(128);
		connection.Property(x => x.ProviderId).HasMaxLength(256);
		connection.Property(x => x.ProviderName).HasMaxLength(256);
		connection.Property(x => x.ConnectionStatus).HasConversion<string>().HasMaxLength(32);
		connection.Property(x => x.SyncStatus).HasConversion<string>().HasMaxLength(32);
		connection.Property(x => x.LastSyncFailureCode).HasMaxLength(64);
		connection.HasIndex(x => x.CredentialsId).IsUnique();
		connection.HasIndex(x => x.FourMoneyUserId);
		connection.HasOne<ZPI4M.User.Models.User>().WithMany().HasForeignKey(x => x.FourMoneyUserId).OnDelete(DeleteBehavior.Restrict);

		foreach (var entity in new[] { attempt.Metadata, connection.Metadata })
		{
			foreach (var property in entity.GetProperties())
			{
				property.SetColumnName(property.Name == "Id" ? "_id" : Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
			}
		}
	}
}
