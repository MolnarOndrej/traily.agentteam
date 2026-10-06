using Microsoft.AspNetCore.DataProtection;

namespace Traily.AgentTeam.Configuration;

public sealed class AccessTokenProtector(IDataProtectionProvider provider)
{
    public string Protect(string connectionId, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return CreateProtector(connectionId).Protect(accessToken.Trim());
    }

    public string Unprotect(string connectionId, string protectedAccessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedAccessToken);
        return CreateProtector(connectionId).Unprotect(protectedAccessToken);
    }

    private IDataProtector CreateProtector(string connectionId) =>
        provider.CreateProtector("Traily", "YouTrackAccessToken", "v1", connectionId);
}
