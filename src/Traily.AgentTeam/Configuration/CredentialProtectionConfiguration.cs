using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Traily.AgentTeam.Configuration;

public static class CredentialProtectionConfiguration
{
    public static void Register(IServiceCollection services)
    {
        // Resolve only when credentials are needed: diagnostics and empty setup do not require keys.
        services.AddSingleton<IDataProtectionProvider>(_ => CreateProvider());
        services.AddSingleton<AccessTokenProtector>();
    }

    private static IDataProtectionProvider CreateProvider()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TRAILY_DATA_PROTECTION_KEYS_PATH");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(configuredPath) && string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("Configure a persistent credential-protection key directory.");
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(localAppData, "Traily", "keys")
            : Path.GetFullPath(configuredPath);
        var certificatePath = Environment.GetEnvironmentVariable("TRAILY_DATA_PROTECTION_CERTIFICATE_PATH");

        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            if (!OperatingSystem.IsWindows())
                throw new InvalidOperationException("Configure a credential-protection certificate on this platform.");
            return DataProtectionProvider.Create(new DirectoryInfo(path), builder =>
            {
                builder.SetApplicationName("Traily.AgentTeam");
                if (OperatingSystem.IsWindows())
                    builder.ProtectKeysWithDpapi();
            });
        }

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            Environment.GetEnvironmentVariable("TRAILY_DATA_PROTECTION_CERTIFICATE_PASSWORD"),
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The credential-protection certificate requires a private key.");
        // Data Protection needs the private key for later reads; retain its own certificate instance.
        var retainedCertificate = new X509Certificate2(certificate);
        return DataProtectionProvider.Create(new DirectoryInfo(path), builder =>
        {
            builder.SetApplicationName("Traily.AgentTeam");
            builder.ProtectKeysWithCertificate(retainedCertificate);
        });
    }
}
