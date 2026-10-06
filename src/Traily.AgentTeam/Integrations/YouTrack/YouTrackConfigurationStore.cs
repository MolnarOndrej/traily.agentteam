using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed record YouTrackConnectionSettings(string ConnectionId, Uri BaseAddress, string AccessToken)
{
    public override string ToString() => $"YouTrack connection {ConnectionId}";
}

public sealed record YouTrackSourceSettings(
    ManagedProject Project, YouTrackProjectConfiguration Configuration,
    YouTrackConnectionSettings Connection);

public sealed class YouTrackConfigurationStore(
    IServiceScopeFactory scopeFactory, Lazy<AccessTokenProtector> tokenProtector,
    TimeProvider timeProvider)
{
    public async Task<YouTrackConnectionSettings> GetConnectionAsync(
        string connectionId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
        var connection = await database.WorkSourceConnections.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.Id == connectionId, cancellationToken);
        var credentials = await database.YouTrackConnectionConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.ConnectionId == connectionId, cancellationToken);
        if (connection is null ||
            !string.Equals(connection.ProviderId, "YouTrack", StringComparison.OrdinalIgnoreCase) ||
            credentials is null || string.IsNullOrWhiteSpace(credentials.ProtectedAccessToken))
            throw Invalid("Configure this YouTrack connection and its protected access token.");

        if (!Uri.TryCreate(connection.BaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            throw Invalid("Configure an HTTPS service URL without credentials, query, or fragment.");

        string token;
        try
        {
            token = tokenProtector.Value.Unprotect(connectionId, credentials.ProtectedAccessToken);
        }
        catch (Exception exception) when (exception is CryptographicException or
            InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Invalid("The connection credential could not be decrypted. Verify the protected key store and service identity.");
        }
        if (string.IsNullOrWhiteSpace(token))
            throw Invalid("Configure a nonempty connection credential.");
        return new(connectionId, address, token);
    }

    public async Task<YouTrackSourceSettings> GetSourceAsync(
        string sourceId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
        var project = await database.ManagedProjects.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.SourceId == sourceId, cancellationToken);
        var configuration = await database.YouTrackProjectConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.SourceId == sourceId, cancellationToken);
        if (project is null || configuration is null)
            throw Invalid("Configure the mapped project and its YouTrack discovery settings.");
        if (!Regex.IsMatch(project.ExternalProjectId, @"\A[0-9]+-[0-9]+\z"))
            throw Invalid("Configure the stable YouTrack project database ID.");
        ValidateProjectConfiguration(configuration);
        var connection = await GetConnectionAsync(project.WorkSourceConnectionId, cancellationToken);
        return new(project, configuration, connection);
    }

    public async Task SetAccessTokenAsync(
        string connectionId, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
        var connection = await database.WorkSourceConnections.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.Id == connectionId, cancellationToken);
        if (connection is null || !string.Equals(connection.ProviderId, "YouTrack", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Select an existing YouTrack connection before configuring its credential.");
        var protectedToken = tokenProtector.Value.Protect(connectionId, accessToken);
        var configuration = await database.YouTrackConnectionConfigurations
            .SingleOrDefaultAsync(entry => entry.ConnectionId == connectionId, cancellationToken);
        if (configuration is null)
        {
            configuration = new() { ConnectionId = connectionId };
            database.YouTrackConnectionConfigurations.Add(configuration);
        }
        configuration.ProtectedAccessToken = protectedToken;
        configuration.UpdatedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
    }

    public static void ValidateProjectConfiguration(YouTrackProjectConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.DiscoveryQueryTemplate) ||
            configuration.DiscoveryQueryTemplate.Length > 4000 ||
            !configuration.DiscoveryQueryTemplate.Contains("{{project}}", StringComparison.Ordinal) ||
            !configuration.DiscoveryQueryTemplate.Contains("{{assignee}}", StringComparison.Ordinal))
            throw Invalid("The discovery query template must include {{project}} and {{assignee}} placeholders.");
        var remainingTemplate = configuration.DiscoveryQueryTemplate
            .Replace("{{project}}", string.Empty, StringComparison.Ordinal)
            .Replace("{{assignee}}", string.Empty, StringComparison.Ordinal)
            .Replace("{{stateField}}", string.Empty, StringComparison.Ordinal)
            .Replace("{{assigneeField}}", string.Empty, StringComparison.Ordinal);
        if (remainingTemplate.Contains("{{", StringComparison.Ordinal) ||
            remainingTemplate.Contains("}}", StringComparison.Ordinal))
            throw Invalid("The discovery query template contains an unsupported placeholder.");
        ValidateSearchValue(configuration.WorkflowStateField);
        ValidateSearchValue(configuration.AssigneeField);
    }

    public static string CreateDiscoveryQuery(
        YouTrackProjectConfiguration configuration, string projectShortName, string assigneeLogin)
    {
        ValidateProjectConfiguration(configuration);
        var query = configuration.DiscoveryQueryTemplate
            .Replace("{{project}}", QuoteSearchValue(projectShortName), StringComparison.Ordinal)
            .Replace("{{assignee}}", QuoteSearchValue(assigneeLogin), StringComparison.Ordinal)
            .Replace("{{stateField}}", QuoteSearchValue(configuration.WorkflowStateField), StringComparison.Ordinal)
            .Replace("{{assigneeField}}", QuoteSearchValue(configuration.AssigneeField), StringComparison.Ordinal);
        if (query.Contains("{{", StringComparison.Ordinal) || query.Contains("}}", StringComparison.Ordinal))
            throw Invalid("The discovery query template contains an unsupported placeholder.");
        return query;
    }

    private static string QuoteSearchValue(string value)
    {
        ValidateSearchValue(value);
        return "{" + value.Trim() + "}";
    }

    private static void ValidateSearchValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            value.Any(character => char.IsControl(character) || character is '{' or '}' or '\\'))
            throw Invalid("A YouTrack search field or identity cannot be represented safely in this query.");
    }

    private static YouTrackConfigurationException Invalid(string diagnostic) => new(diagnostic);
}
