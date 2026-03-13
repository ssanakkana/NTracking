using Microsoft.Data.Sqlite;

namespace NTracking.Infrastructure.Storage;

public sealed class SqliteConnectionFactory
{
	private readonly StorageOptions options;

	public SqliteConnectionFactory(StorageOptions options)
	{
		this.options = options;
	}

	public SqliteConnection CreateOpenConnection()
	{
		String connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = options.DatabasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared,
			Pooling = true,
		}.ToString();

		SqliteConnection connection = new SqliteConnection(connectionString);
		connection.Open();

		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA busy_timeout=5000;";
		command.ExecuteNonQuery();

		return connection;
	}
}

