using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankingConnectionRepository;
using ZPI4M.Banking.Repositories.BankRefreshRepository;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.Tests;

public sealed class BankingContextRegistrationTests
{
	[Fact]
	public async Task FactoryCoexistsWithScopedContextAndAllBankingServices()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddTink(new ConfigurationBuilder().Build());
		services.AddOptions<BankRefreshOptions>();
		services.AddOptions<BankingConnectionOptions>();
		const string connection = "Host=localhost;Database=synthetic;Username=synthetic;Password=synthetic";
		services.AddDbContextFactory<BankingDbContext>(o => o.UseNpgsql(connection), ServiceLifetime.Scoped);
		services.AddDbContext<UserDbContext>(o => o.UseNpgsql(connection));
		services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
		services.AddScoped<BankingConnectionRepository>();
		services.AddScoped<IBankConnectionRepository>(p => p.GetRequiredService<BankingConnectionRepository>());
		services.AddScoped<IBankConnectionAttemptRepository>(p => p.GetRequiredService<BankingConnectionRepository>());
		services.AddScoped<BankingConnectionService>();
		services.AddScoped<IBankAccountRepository, BankAccountRepository>();
		services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();
		services.AddScoped<IBankingReadService, BankingReadService>();
		services.AddScoped<IBankTransactionAssignmentRepository, BankTransactionAssignmentRepository>();
		services.AddScoped<IBankTransactionAssignmentService, BankTransactionAssignmentService>();
		services.AddScoped<BankRefreshRepository>();
		services.AddScoped<BankRefreshService>();
		services.AddScoped<BankRefreshProcessor>();
		services.AddHostedService<BankRefreshWorker>();
		await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		await using var scope = provider.CreateAsyncScope();
		var scoped = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
		Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<BankingDbContext>());
		var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankingDbContext>>();
		await using var owned = await factory.CreateDbContextAsync();
		Assert.NotSame(scoped, owned);
		Assert.Equal(scoped.Database.GetConnectionString(), owned.Database.GetConnectionString());
		foreach (var type in new[] { typeof(BankingConnectionService), typeof(IBankingReadService),
			typeof(IBankTransactionAssignmentService), typeof(BankRefreshService), typeof(BankRefreshProcessor) })
			Assert.NotNull(scope.ServiceProvider.GetRequiredService(type));
		Assert.IsType<BankRefreshWorker>(Assert.Single(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()));
		await using var secondScope = provider.CreateAsyncScope();
		Assert.NotSame(scoped, secondScope.ServiceProvider.GetRequiredService<BankingDbContext>());
	}
}
