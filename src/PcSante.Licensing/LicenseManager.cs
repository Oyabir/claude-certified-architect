using PcSante.Core.Commands;
using PcSante.Core.Licensing;

namespace PcSante.Licensing;

public sealed record LicenseOperationResult(bool Success, FailureReason Reason, string MessageKey, LicenseStatus Status)
{
    public int? TransfersRemaining { get; init; }
}

/// <summary>
/// Gestion de la licence côté PC (section 12). Seul un jeton signé par le serveur débloque Premium ;
/// l'application ne se fait jamais confiance à elle-même.
/// </summary>
public sealed class LicenseManager
{
    /// <summary>Tolérance d'horloge (changement d'heure, dérive) avant de considérer un recul volontaire.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(2);

    private readonly ILicenseServerClient _server;
    private readonly ILicenseStateStore _store;
    private readonly IHardwareInfoProvider _hardware;
    private readonly TimeProvider _time;
    private readonly byte[]? _publicKey;
    private readonly object _gate = new();
    private HardwareFingerprint? _fingerprint;

    /// <param name="publicKeyBase64">Clé publique Ed25519 intégrée à la compilation ; vide = non configurée.</param>
    public LicenseManager(ILicenseServerClient server, ILicenseStateStore store, IHardwareInfoProvider hardware, TimeProvider time, string? publicKeyBase64)
    {
        _server = server;
        _store = store;
        _hardware = hardware;
        _time = time;
        _publicKey = DecodeKey(publicKeyBase64);
    }

    public bool IsConfigured => _publicKey is not null;

    public HardwareFingerprint Fingerprint => _fingerprint ??= HardwareFingerprint.Compute(_hardware.Read());

#if PCSANTE_TEST_PREMIUM
    public const bool IsTestPremiumBuild = true;
#else
    /// <summary>Vrai uniquement dans un build de test (build.ps1 -TestPremium) ; faux dans tout build de production.</summary>
    public const bool IsTestPremiumBuild = false;
#endif

    /// <summary>État actuel, calculé hors ligne à partir du jeton stocké.</summary>
    public LicenseStatus GetStatus()
    {
#if PCSANTE_TEST_PREMIUM
        // Build de TEST : toutes les fonctions Premium sans licence (jamais compilé en production).
        return new LicenseStatus { State = LicenseState.Active, EffectiveTier = LicenseTier.Premium, KeyHint = "TEST", Seats = 1 };
#else
        if (_publicKey is null)
        {
            return LicenseStatus.Free(LicenseState.NotConfigured);
        }

        lock (_gate)
        {
            var state = _store.Load();
            var now = _time.GetUtcNow();

            // Contrôle de l'horloge : un recul de la date bloque le jeton jusqu'à la prochaine revalidation en ligne.
            if (state.ClockTampered || (state.HighWaterMark != default && now < state.HighWaterMark - ClockTolerance))
            {
                if (!state.ClockTampered)
                {
                    _store.Save(state with { ClockTampered = true });
                }

                return LicenseStatus.Free(LicenseState.ClockTampered) with { KeyHint = TryPayload(state)?.KeyHint };
            }

            if (now > state.HighWaterMark + TimeSpan.FromMinutes(10))
            {
                state = state with { HighWaterMark = now };
                _store.Save(state);
            }

            return Evaluate(state, now);
        }
#endif
    }

    public bool IsRevalidationDue()
    {
        var status = GetStatus();
        if (status.State is LicenseState.NotConfigured or LicenseState.NotActivated or LicenseState.Revoked)
        {
            return false;
        }

        var last = _store.Load().LastValidatedAt;
        return last is null || _time.GetUtcNow() - last.Value >= LicenseTokenCodec.RevalidationInterval
            || status.State is LicenseState.ClockTampered or LicenseState.OfflineTooLong;
    }

    public async Task<LicenseOperationResult> ActivateAsync(string licenseKey, CancellationToken cancellationToken)
    {
        if (_publicKey is null)
        {
            return Fail(FailureReason.LicenseNotConfigured, "License_NotConfigured");
        }

        var key = LicenseKeyFormat.Normalize(licenseKey);
        if (key is null)
        {
            return Fail(FailureReason.LicenseInvalidKey, "License_InvalidKey");
        }

        var response = await _server.ActivateAsync(
            new ActivationRequest(key, Fingerprint.Components, Core.ProductInfo.Version), cancellationToken).ConfigureAwait(false);
        return Apply(response, key);
    }

    public async Task<LicenseOperationResult> TransferAsync(string licenseKey, CancellationToken cancellationToken)
    {
        if (_publicKey is null)
        {
            return Fail(FailureReason.LicenseNotConfigured, "License_NotConfigured");
        }

        var key = LicenseKeyFormat.Normalize(licenseKey);
        if (key is null)
        {
            return Fail(FailureReason.LicenseInvalidKey, "License_InvalidKey");
        }

        var response = await _server.TransferAsync(new TransferRequest(key, Fingerprint.Components), cancellationToken).ConfigureAwait(false);
        return Apply(response, key, "License_Transferred");
    }

    /// <summary>Revalidation (tous les 7 jours). Hors ligne : rien ne change, le jeton reste valable jusqu'à expiration.</summary>
    public async Task<LicenseOperationResult> RevalidateAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load();
        var payload = TryPayload(state);
        if (_publicKey is null || payload is null || state.Revoked)
        {
            return Fail(FailureReason.PreconditionFailed, "License_NotActivated");
        }

        var response = await _server.RevalidateAsync(
            new RevalidationRequest(payload.KeyHash, payload.ActivationId, Fingerprint.Components), cancellationToken).ConfigureAwait(false);
        return Apply(response, null, "License_Revalidated");
    }

    public async Task<LicenseOperationResult> DeactivateAsync(CancellationToken cancellationToken)
    {
        var state = _store.Load();
        var payload = TryPayload(state);
        if (_publicKey is null || payload is null)
        {
            return Fail(FailureReason.PreconditionFailed, "License_NotActivated");
        }

        var response = await _server.DeactivateAsync(
            new DeactivationRequest(payload.KeyHash, payload.ActivationId, Fingerprint.Components), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return Fail(FailureReason.NetworkUnavailable, "License_ServerUnreachable");
        }

        if (!response.Ok && response.Error != LicenseErrorCode.NotActivated)
        {
            return Fail(MapError(response.Error), MessageFor(response.Error));
        }

        lock (_gate)
        {
            _store.Save(new StoredLicenseState { HighWaterMark = Later(state.HighWaterMark, response.ServerTime) });
        }

        return new LicenseOperationResult(true, FailureReason.None, "License_Deactivated", GetStatus());
    }

    private LicenseOperationResult Apply(LicenseServerResponse? response, string? normalizedKey, string successKey = "License_Activated")
    {
        if (response is null)
        {
            return Fail(FailureReason.NetworkUnavailable, "License_ServerUnreachable");
        }

        lock (_gate)
        {
            var state = _store.Load();
            if (!response.Ok)
            {
                if (response.Error is LicenseErrorCode.Revoked or LicenseErrorCode.Expired
                    && (normalizedKey is null || TryPayload(state)?.KeyHash == LicenseKeyFormat.Hash(normalizedKey)))
                {
                    // Révocation/expiration : retour automatique à l'offre Gratuite, sans perte de données.
                    _store.Save(state with { Token = null, Revoked = response.Error == LicenseErrorCode.Revoked, ClockTampered = false });
                }

                return Fail(MapError(response.Error), MessageFor(response.Error)) with { TransfersRemaining = response.TransfersRemaining };
            }

            var payload = LicenseTokenCodec.Verify(response.Token, _publicKey!);
            if (payload is null
                || !new HardwareFingerprint(payload.Fingerprint).Matches(Fingerprint)
                || (normalizedKey is not null && payload.KeyHash != LicenseKeyFormat.Hash(normalizedKey)))
            {
                return Fail(FailureReason.LicenseInvalidKey, "License_InvalidServerAnswer");
            }

            // Une réponse signée récente du serveur fait foi pour l'heure : l'éventuel blocage d'horloge est levé.
            var now = _time.GetUtcNow();
            _store.Save(new StoredLicenseState
            {
                Token = response.Token,
                HighWaterMark = payload.IssuedAt,
                LastValidatedAt = now,
                Revoked = false,
                ClockTampered = false,
            });
        }

        return new LicenseOperationResult(true, FailureReason.None, successKey, GetStatus()) { TransfersRemaining = response.TransfersRemaining };
    }

    private LicenseStatus Evaluate(StoredLicenseState state, DateTimeOffset now)
    {
        if (state.Revoked)
        {
            return LicenseStatus.Free(LicenseState.Revoked);
        }

        if (string.IsNullOrEmpty(state.Token))
        {
            return LicenseStatus.Free(LicenseState.NotActivated);
        }

        var payload = LicenseTokenCodec.Verify(state.Token, _publicKey!);
        if (payload is null)
        {
            return LicenseStatus.Free(LicenseState.InvalidToken);
        }

        var status = new LicenseStatus
        {
            State = LicenseState.Active,
            EffectiveTier = payload.Tier,
            KeyHint = payload.KeyHint,
            TokenExpiresAt = payload.ExpiresAt,
            LicenseExpiresAt = payload.LicenseExpiresAt,
            LastValidatedAt = state.LastValidatedAt,
            Seats = payload.Seats,
        };

        if (!new HardwareFingerprint(payload.Fingerprint).Matches(Fingerprint))
        {
            return status with { State = LicenseState.WrongComputer, EffectiveTier = LicenseTier.Free };
        }

        if (now + ClockTolerance < payload.IssuedAt)
        {
            return status with { State = LicenseState.ClockTampered, EffectiveTier = LicenseTier.Free };
        }

        if (payload.LicenseExpiresAt is { } end && now >= end)
        {
            return status with { State = LicenseState.Expired, EffectiveTier = LicenseTier.Free };
        }

        if (now >= payload.ExpiresAt)
        {
            return status with { State = LicenseState.OfflineTooLong, EffectiveTier = LicenseTier.Free };
        }

        return status;
    }

    private LicenseTokenPayload? TryPayload(StoredLicenseState state) =>
        _publicKey is null ? null : LicenseTokenCodec.Verify(state.Token, _publicKey);

    private LicenseOperationResult Fail(FailureReason reason, string messageKey) =>
        new(false, reason, messageKey, GetStatus());

    internal static FailureReason MapError(LicenseErrorCode error) => error switch
    {
        LicenseErrorCode.InvalidKey or LicenseErrorCode.InvalidRequest => FailureReason.LicenseInvalidKey,
        LicenseErrorCode.Revoked => FailureReason.LicenseRevoked,
        LicenseErrorCode.Expired => FailureReason.LicenseExpired,
        LicenseErrorCode.AlreadyUsedElsewhere => FailureReason.LicenseUsedElsewhere,
        LicenseErrorCode.TransferLimitReached => FailureReason.LicenseTransferLimit,
        LicenseErrorCode.NotActivated => FailureReason.PreconditionFailed,
        LicenseErrorCode.RateLimited => FailureReason.NetworkUnavailable,
        _ => FailureReason.InternalError,
    };

    internal static string MessageFor(LicenseErrorCode error) => error switch
    {
        LicenseErrorCode.InvalidKey or LicenseErrorCode.InvalidRequest => "License_InvalidKey",
        LicenseErrorCode.Revoked => "License_Revoked",
        LicenseErrorCode.Expired => "License_Expired",
        LicenseErrorCode.AlreadyUsedElsewhere => "License_UsedElsewhere",
        LicenseErrorCode.TransferLimitReached => "License_TransferLimit",
        LicenseErrorCode.NotActivated => "License_NotActivated",
        LicenseErrorCode.RateLimited => "License_RateLimited",
        _ => "License_ServerError",
    };

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static byte[]? DecodeKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var key = Convert.FromBase64String(base64.Trim());
            return key.Length == Ed25519.KeySize ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
