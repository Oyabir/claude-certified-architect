using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Licensing;

namespace PcSante.Licensing;

/// <summary>Contenu signé du jeton de licence.</summary>
public sealed record LicenseTokenPayload
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>SHA-256 de la clé normalisée : le jeton ne contient jamais la clé.</summary>
    public required string KeyHash { get; init; }

    public required string KeyHint { get; init; }

    public required Guid ActivationId { get; init; }

    public required IReadOnlyList<string> Fingerprint { get; init; }

    public required LicenseTier Tier { get; init; }

    public int Seats { get; init; } = 1;

    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>Fin de validité du jeton (hors ligne toléré 14 jours).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Fin de l'abonnement, si limité.</summary>
    public DateTimeOffset? LicenseExpiresAt { get; init; }
}

/// <summary>Format : base64url(JSON) + "." + base64url(signature Ed25519 du JSON).</summary>
public static class LicenseTokenCodec
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromDays(7);

    public static string Sign(LicenseTokenPayload payload, byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, PcSanteJson.Options);
        var signature = Ed25519.Sign(privateKey, json);
        return $"{Base64Url.EncodeToString(json)}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>Renvoie le contenu si et seulement si la signature est valide pour la clé publique fournie.</summary>
    public static LicenseTokenPayload? Verify(string? token, byte[] publicKey)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 8192)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var json = Base64Url.DecodeFromChars(parts[0]);
            var signature = Base64Url.DecodeFromChars(parts[1]);
            if (!Ed25519.Verify(publicKey, json, signature))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<LicenseTokenPayload>(json, PcSanteJson.Options);
            return payload is { Version: LicenseTokenPayload.CurrentVersion } && HardwareFingerprint.IsWellFormed(payload.Fingerprint)
                ? payload
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Encodage base64url sans remplissage (RFC 4648 §5).</summary>
internal static class Base64Url
{
    public static string EncodeToString(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DecodeFromChars(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            0 => s,
            _ => throw new FormatException("base64url invalide"),
        };
        return Convert.FromBase64String(s);
    }
}
