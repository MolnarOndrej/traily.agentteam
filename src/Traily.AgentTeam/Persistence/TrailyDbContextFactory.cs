using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Traily.AgentTeam.Configuration;

namespace Traily.AgentTeam.Persistence;

public sealed class TrailyDbContextFactory
    : IDesignTimeDbContextFactory<TrailyDbContext>
{
    public TrailyDbContext CreateDbContext(string[] args)
    {
        var databasePath =
            DatabaseStorageConfiguration.ResolvePath();

        DatabaseStorageConfiguration.EnsureDirectoryExists(
            databasePath);

        var options = new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(
                DatabaseStorageConfiguration.CreateConnectionString(
                    databasePath))
            .Options;

        return new TrailyDbContext(options);
    }
}