using Npgsql;

namespace ZPI4M.Banking.IntegrationTests;

internal static class SeedScript
{
	public static async Task ExecuteAsync(string connectionString, string sql)
	{
		await using var connection = new NpgsqlConnection(connectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync();
	}
}
