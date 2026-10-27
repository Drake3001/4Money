using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZPI4M.Api.Services;
using ZPI4M.Database;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class RebaseCompatibilityTests
{
	[Theory]
	[InlineData("sub")]
	[InlineData(ClaimTypes.NameIdentifier)]
	public void BankingAcceptsUserIdFromAuthenticatedIdentity(string claimType)
	{
		var userId = Guid.NewGuid();
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, userId.ToString())], "Test"))
		};
		var currentUser = new HttpBankingCurrentUser(new HttpContextAccessor { HttpContext = context });

		Assert.Equal(userId, currentUser.UserId);
	}

	[Fact]
	public void BankingRejectsUserIdFromUnauthenticatedIdentity()
	{
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]))
		};
		var currentUser = new HttpBankingCurrentUser(new HttpContextAccessor { HttpContext = context });

		Assert.Null(currentUser.UserId);
	}

	[Fact]
	public void MergedSnapshotMatchesCurrentModel()
	{
		using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseNpgsql("Host=localhost;Database=model_check").Options);

		Assert.False(context.Database.HasPendingModelChanges());
	}

	[Fact]
	public void LatestMigrationTargetMatchesCurrentModel()
	{
		using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseNpgsql("Host=localhost;Database=model_check").Options);
		var migrations = context.GetService<IMigrationsAssembly>();
		var latest = migrations.CreateMigration(migrations.Migrations.Last().Value, context.Database.ProviderName!);
		var target = context.GetService<IModelRuntimeInitializer>().Initialize(latest.TargetModel, designTime: true);
		var current = context.GetService<IDesignTimeModel>().Model;

		Assert.False(context.GetService<IMigrationsModelDiffer>().HasDifferences(
			target.GetRelationalModel(), current.GetRelationalModel()));
	}
}
