using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ZPI4M.Api.Configuration;

public static class DatabaseConfiguration
{
	public static void ConfigurePostgres(DbContextOptionsBuilder options, string connectionString) =>
		options.UseNpgsql(connectionString,
			npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"));

	public static string GetPostgresConnectionString(this IConfiguration configuration) => new NpgsqlConnectionStringBuilder
	{
		Host = configuration["POSTGRES_HOST"] ?? "127.0.0.1",
		Port = configuration.GetValue<int>("POSTGRES_PORT"),
		Database = configuration["POSTGRES_DB"],
		Username = configuration["POSTGRES_USER"],
		Password = configuration["POSTGRES_PASSWORD"]
	}.ConnectionString;
}
