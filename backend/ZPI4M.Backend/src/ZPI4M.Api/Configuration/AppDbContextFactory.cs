using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZPI4M.Database;

namespace ZPI4M.Api.Configuration;

// EF tools and the deployment bundle need only database configuration, not the API host.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
	public AppDbContext CreateDbContext(string[] args)
	{
		if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
			Env.Load("../../../../containers/local/.env");

		var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
		var connectionString = configuration.GetPostgresConnectionString();
		var options = new DbContextOptionsBuilder<AppDbContext>();
		DatabaseConfiguration.ConfigurePostgres(options, connectionString);
		return new AppDbContext(options.Options);
	}
}
