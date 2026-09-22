using Microsoft.Data.Sqlite;

namespace Traily.AgentTeam.Configuration;

public static class DatabaseStorageConfiguration
{
    public const string PathEnvironmentVariable =
        "TRAILY_DATABASE_PATH";

    private const string ApplicationDirectoryName = "Traily";
    private const string DataDirectoryName = "data";
    private const string DatabaseFileName = "traily.db";

    public static string ResolvePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(
            PathEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                $"Cannot determine the default database path. " +
                $"Configure {PathEnvironmentVariable}.");
        }

        return Path.Combine(
            localAppData,
            ApplicationDirectoryName,
            DataDirectoryName,
            DatabaseFileName);
    }

    public static void EnsureDirectoryExists(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(databasePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                $"Cannot determine the database directory for " +
                $"'{databasePath}'.");
        }

        Directory.CreateDirectory(directory);
    }

    public static string CreateConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }
}