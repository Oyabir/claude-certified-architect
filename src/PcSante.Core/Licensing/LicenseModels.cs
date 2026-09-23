namespace PcSante.Core.Licensing;

public enum LicenseTier
{
    Free,
    Premium,
    Family,
}

/// <summary>État de la licence tel que connu du service.</summary>
public enum LicenseState
{
    /// <summary>Clé publique absente de la compilation : offre Gratuite seulement.</summary>
    NotConfigured,
    NotActivated,
    Active,
    /// <summary>Jeton expiré (plus de 14 jours sans connexion) : Gratuite jusqu'à la prochaine connexion.</summary>
    OfflineTooLong,
    Expired,
    Revoked,
    /// <summary>La date du PC a reculé : jeton bloqué jusqu'à revalidation en ligne.</summary>
    ClockTampered,
    /// <summary>Jeton émis pour un autre PC.</summary>
    WrongComputer,
    InvalidToken,
}

public sealed record LicenseStatus
{
    public required LicenseState State { get; init; }

    /// <summary>Offre effectivement accordée (Gratuite dès que le jeton n'est pas valide).</summary>
    public required LicenseTier EffectiveTier { get; init; }

    public string? KeyHint { get; init; }

    public DateTimeOffset? TokenExpiresAt { get; init; }

    public DateTimeOffset? LicenseExpiresAt { get; init; }

    public DateTimeOffset? LastValidatedAt { get; init; }

    public int Seats { get; init; }

    public bool IsPremium => EffectiveTier != LicenseTier.Free;

    public static LicenseStatus Free(LicenseState state) =>
        new() { State = state, EffectiveTier = LicenseTier.Free };
}
