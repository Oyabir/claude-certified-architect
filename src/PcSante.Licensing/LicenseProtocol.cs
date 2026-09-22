using PcSante.Core.Licensing;

namespace PcSante.Licensing;

/// <summary>Codes d'erreur du serveur de licences (jamais affichés tels quels à l'utilisateur).</summary>
public enum LicenseErrorCode
{
    None,
    InvalidRequest,
    InvalidKey,
    Revoked,
    Expired,
    AlreadyUsedElsewhere,
    TransferLimitReached,
    NotActivated,
    RateLimited,
    ServerError,
}

public sealed record ActivationRequest(string LicenseKey, IReadOnlyList<string> Fingerprint, string? AppVersion);

/// <summary>La clé n'est pas conservée en clair sur le PC : la revalidation utilise son hachage.</summary>
public sealed record RevalidationRequest(string KeyHash, Guid ActivationId, IReadOnlyList<string> Fingerprint);

public sealed record TransferRequest(string LicenseKey, IReadOnlyList<string> Fingerprint);

public sealed record DeactivationRequest(string KeyHash, Guid ActivationId, IReadOnlyList<string> Fingerprint);

public sealed record LicenseServerResponse
{
    public bool Ok { get; init; }

    public LicenseErrorCode Error { get; init; }

    public string? Token { get; init; }

    public DateTimeOffset ServerTime { get; init; }

    /// <summary>Transferts encore possibles sur 12 mois glissants.</summary>
    public int? TransfersRemaining { get; init; }

    public static LicenseServerResponse Success(string token, DateTimeOffset now, int? transfersRemaining = null) =>
        new() { Ok = true, Token = token, ServerTime = now, TransfersRemaining = transfersRemaining };

    public static LicenseServerResponse Fail(LicenseErrorCode error, DateTimeOffset now, int? transfersRemaining = null) =>
        new() { Ok = false, Error = error, ServerTime = now, TransfersRemaining = transfersRemaining };
}

/// <summary>Manifeste de mise à jour signé par le serveur (Ed25519), vérifié par le service avant tout téléchargement.</summary>
public sealed record UpdateManifest(string Version, Uri DownloadUrl, string Sha256, DateTimeOffset PublishedAt);

public sealed record SignedUpdateManifest(string Manifest, string Signature);

public static class LicenseProtocol
{
    public const string ActivatePath = "api/v1/activate";
    public const string RevalidatePath = "api/v1/revalidate";
    public const string TransferPath = "api/v1/transfer";
    public const string DeactivatePath = "api/v1/deactivate";
    public const string UpdatePath = "api/v1/updates/latest";

    public const int MaxTransfersPerYear = 2;

    public static bool IsPaidTier(LicenseTier tier) => tier != LicenseTier.Free;
}
