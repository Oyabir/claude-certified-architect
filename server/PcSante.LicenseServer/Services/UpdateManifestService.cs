using System.Text;
using System.Text.Json;
using PcSante.Core;
using PcSante.Licensing;

namespace PcSante.LicenseServer.Services;

/// <summary>Configuration de la dernière version publiée (section « Updates » de appsettings).</summary>
public sealed class UpdateOptions
{
    public string? Version { get; set; }

    public Uri? DownloadUrl { get; set; }

    public string? Sha256 { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>Signe le manifeste de mise à jour avec la clé Ed25519 du serveur.</summary>
public sealed class UpdateManifestService(UpdateOptions options, ServerKeys keys)
{
    public SignedUpdateManifest? GetSigned()
    {
        if (string.IsNullOrWhiteSpace(options.Version) || options.DownloadUrl is null
            || options.DownloadUrl.Scheme != Uri.UriSchemeHttps || options.Sha256 is not { Length: 64 })
        {
            return null;
        }

        var manifest = new UpdateManifest(options.Version, options.DownloadUrl, options.Sha256.ToUpperInvariant(), options.PublishedAt ?? DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(manifest, PcSanteJson.Options);
        var signature = Ed25519.Sign(keys.PrivateKey, Encoding.UTF8.GetBytes(json));
        return new SignedUpdateManifest(json, Convert.ToBase64String(signature));
    }
}
