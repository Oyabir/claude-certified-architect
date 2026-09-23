using Microsoft.EntityFrameworkCore;
using PcSante.Core.Licensing;
using PcSante.LicenseServer.Data;
using PcSante.Licensing;

namespace PcSante.LicenseServer.Services;

/// <summary>Règles d'activation (section 12). Chaque refus est journalisé.</summary>
public sealed partial class LicenseService(LicenseDbContext db, ServerKeys keys, TimeProvider time, ILogger<LicenseService> logger)
{
    public static readonly TimeSpan TransferWindow = TimeSpan.FromDays(365);

    private readonly ILogger<LicenseService> _logger = logger;

    public async Task<LicenseServerResponse> ActivateAsync(ActivationRequest request, string? ip, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var key = LicenseKeyFormat.Normalize(request?.LicenseKey);
        if (request is null || key is null || !HardwareFingerprint.IsWellFormed(request.Fingerprint)
            || new HardwareFingerprint(request.Fingerprint).ReadableCount < HardwareFingerprint.MinimumReadable)
        {
            return await RefuseAsync("activate", ip, null, LicenseErrorCode.InvalidRequest, ct).ConfigureAwait(false);
        }

        var license = await FindByHashAsync(LicenseKeyFormat.Hash(key), ct).ConfigureAwait(false);
        if (await CheckLicenseAsync(license, "activate", ip, key, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var fingerprint = new HardwareFingerprint(request.Fingerprint);
        var active = license!.Activations.Where(a => a.IsActive).ToList();
        var existing = active.FirstOrDefault(a => new HardwareFingerprint(a.Fingerprint).Matches(fingerprint));
        if (existing is not null)
        {
            // Même PC (réinstallation, disque changé) : réactivation sans nouvelle activation.
            existing.Fingerprint = request.Fingerprint.ToArray();
            existing.LastSeenAt = now;
            existing.AppVersion = Truncate(request.AppVersion, 32);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Success(license, existing, now);
        }

        if (active.Count >= license.Seats)
        {
            return await RefuseAsync("activate", ip, license.KeyHint, LicenseErrorCode.AlreadyUsedElsewhere, ct,
                RemainingTransfers(license, now)).ConfigureAwait(false);
        }

        var activation = NewActivation(license, request.Fingerprint, request.AppVersion, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Success(license, activation, now);
    }

    public async Task<LicenseServerResponse> RevalidateAsync(RevalidationRequest request, string? ip, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (request is null || !IsHash(request.KeyHash) || !HardwareFingerprint.IsWellFormed(request.Fingerprint))
        {
            return await RefuseAsync("revalidate", ip, null, LicenseErrorCode.InvalidRequest, ct).ConfigureAwait(false);
        }

        var license = await FindByHashAsync(request.KeyHash.ToUpperInvariant(), ct).ConfigureAwait(false);
        if (await CheckLicenseAsync(license, "revalidate", ip, null, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var activation = license!.Activations.FirstOrDefault(a => a.Id == request.ActivationId && a.IsActive);
        if (activation is null)
        {
            return await RefuseAsync("revalidate", ip, license.KeyHint, LicenseErrorCode.NotActivated, ct).ConfigureAwait(false);
        }

        if (!new HardwareFingerprint(activation.Fingerprint).Matches(new HardwareFingerprint(request.Fingerprint)))
        {
            return await RefuseAsync("revalidate", ip, license.KeyHint, LicenseErrorCode.AlreadyUsedElsewhere, ct).ConfigureAwait(false);
        }

        activation.Fingerprint = request.Fingerprint.ToArray();
        activation.LastSeenAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Success(license, activation, now);
    }

    /// <summary>« Transférer ma licence » : désactive l'ancien PC, active le nouveau. 2 transferts par an.</summary>
    public async Task<LicenseServerResponse> TransferAsync(TransferRequest request, string? ip, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var key = LicenseKeyFormat.Normalize(request?.LicenseKey);
        if (request is null || key is null || !HardwareFingerprint.IsWellFormed(request.Fingerprint)
            || new HardwareFingerprint(request.Fingerprint).ReadableCount < HardwareFingerprint.MinimumReadable)
        {
            return await RefuseAsync("transfer", ip, null, LicenseErrorCode.InvalidRequest, ct).ConfigureAwait(false);
        }

        var license = await FindByHashAsync(LicenseKeyFormat.Hash(key), ct).ConfigureAwait(false);
        if (await CheckLicenseAsync(license, "transfer", ip, key, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var fingerprint = new HardwareFingerprint(request.Fingerprint);
        var active = license!.Activations.Where(a => a.IsActive).ToList();
        var remaining = RemainingTransfers(license, now);

        var existing = active.FirstOrDefault(a => new HardwareFingerprint(a.Fingerprint).Matches(fingerprint));
        if (existing is not null)
        {
            existing.LastSeenAt = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Success(license, existing, now, remaining);
        }

        if (active.Count < license.Seats)
        {
            // Une place est libre : simple activation, aucun transfert décompté.
            var fresh = NewActivation(license, request.Fingerprint, null, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Success(license, fresh, now, remaining);
        }

        if (remaining <= 0)
        {
            return await RefuseAsync("transfer", ip, license.KeyHint, LicenseErrorCode.TransferLimitReached, ct, 0).ConfigureAwait(false);
        }

        var old = active.OrderBy(a => a.LastSeenAt).First();
        old.DeactivatedAt = now;
        old.DeactivationReason = "transfer";
        var activation = NewActivation(license, request.Fingerprint, null, now);
        db.Transfers.Add(new TransferEntity
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            At = now,
            FromActivationId = old.Id,
            ToActivationId = activation.Id,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogTransfer(license.KeyHint, old.Id, activation.Id);
        return Success(license, activation, now, remaining - 1);
    }

    public async Task<LicenseServerResponse> DeactivateAsync(DeactivationRequest request, string? ip, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (request is null || !IsHash(request.KeyHash) || !HardwareFingerprint.IsWellFormed(request.Fingerprint))
        {
            return await RefuseAsync("deactivate", ip, null, LicenseErrorCode.InvalidRequest, ct).ConfigureAwait(false);
        }

        var license = await FindByHashAsync(request.KeyHash.ToUpperInvariant(), ct).ConfigureAwait(false);
        if (license is null)
        {
            return await RefuseAsync("deactivate", ip, null, LicenseErrorCode.InvalidKey, ct).ConfigureAwait(false);
        }

        var activation = license.Activations.FirstOrDefault(a => a.Id == request.ActivationId && a.IsActive);
        if (activation is null || !new HardwareFingerprint(activation.Fingerprint).Matches(new HardwareFingerprint(request.Fingerprint)))
        {
            return await RefuseAsync("deactivate", ip, license.KeyHint, LicenseErrorCode.NotActivated, ct).ConfigureAwait(false);
        }

        activation.DeactivatedAt = now;
        activation.DeactivationReason = "user";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LicenseServerResponse { Ok = true, ServerTime = now };
    }

    // ----- Administration -----

    public async Task<IReadOnlyList<string>> GenerateKeysAsync(int count, LicenseTier tier, int seats, int? validDays, string? note, CancellationToken ct)
    {
        if (count is < 1 or > 500 || tier == LicenseTier.Free || seats is < 1 or > 50 || validDays is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Paramètres de génération invalides.");
        }

        var now = time.GetUtcNow();
        var keysOut = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var key = LicenseKeyFormat.Generate();
            keysOut.Add(key);
            db.Licenses.Add(new LicenseEntity
            {
                Id = Guid.NewGuid(),
                KeyHash = LicenseKeyFormat.Hash(key),
                KeyHint = LicenseKeyFormat.Hint(key),
                Tier = tier,
                Seats = seats,
                CreatedAt = now,
                ExpiresAt = validDays is { } d ? now.AddDays(d) : null,
                Note = Truncate(note, 256),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return keysOut;
    }

    public async Task<bool> RevokeAsync(Guid licenseId, string? reason, CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return false;
        }

        license.RevokedAt ??= time.GetUtcNow();
        license.RevokedReason = Truncate(reason, 256);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ResetTransfersAsync(Guid licenseId, CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return false;
        }

        license.TransfersResetAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public int RemainingTransfers(LicenseEntity license, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(license);
        var since = now - TransferWindow;
        if (license.TransfersResetAt is { } reset && reset > since)
        {
            since = reset;
        }

        var used = license.Transfers.Count(t => t.At > since);
        return Math.Max(0, LicenseProtocol.MaxTransfersPerYear - used);
    }

    private async Task<LicenseServerResponse?> CheckLicenseAsync(LicenseEntity? license, string endpoint, string? ip, string? key, CancellationToken ct)
    {
        if (license is null)
        {
            return await RefuseAsync(endpoint, ip, key is null ? null : LicenseKeyFormat.Hint(key), LicenseErrorCode.InvalidKey, ct).ConfigureAwait(false);
        }

        if (license.RevokedAt is not null)
        {
            return await RefuseAsync(endpoint, ip, license.KeyHint, LicenseErrorCode.Revoked, ct).ConfigureAwait(false);
        }

        if (license.ExpiresAt is { } end && time.GetUtcNow() >= end)
        {
            return await RefuseAsync(endpoint, ip, license.KeyHint, LicenseErrorCode.Expired, ct).ConfigureAwait(false);
        }

        return null;
    }

    private Task<LicenseEntity?> FindByHashAsync(string hash, CancellationToken ct) =>
        db.Licenses.Include(l => l.Activations).Include(l => l.Transfers).FirstOrDefaultAsync(l => l.KeyHash == hash, ct);

    private ActivationEntity NewActivation(LicenseEntity license, IReadOnlyList<string> fingerprint, string? appVersion, DateTimeOffset now)
    {
        var activation = new ActivationEntity
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            Fp1 = fingerprint[0],
            Fp2 = fingerprint[1],
            Fp3 = fingerprint[2],
            Fp4 = fingerprint[3],
            ActivatedAt = now,
            LastSeenAt = now,
            AppVersion = Truncate(appVersion, 32),
        };
        license.Activations.Add(activation);
        db.Activations.Add(activation);
        return activation;
    }

    private LicenseServerResponse Success(LicenseEntity license, ActivationEntity activation, DateTimeOffset now, int? transfersRemaining = null)
    {
        var payload = new LicenseTokenPayload
        {
            KeyHash = license.KeyHash,
            KeyHint = license.KeyHint,
            ActivationId = activation.Id,
            Fingerprint = activation.Fingerprint,
            Tier = license.Tier,
            Seats = license.Seats,
            IssuedAt = now,
            ExpiresAt = now + LicenseTokenCodec.TokenLifetime,
            LicenseExpiresAt = license.ExpiresAt,
        };
        return LicenseServerResponse.Success(LicenseTokenCodec.Sign(payload, keys.PrivateKey), now, transfersRemaining ?? RemainingTransfers(license, now));
    }

    private async Task<LicenseServerResponse> RefuseAsync(string endpoint, string? ip, string? keyHint, LicenseErrorCode error, CancellationToken ct, int? transfersRemaining = null)
    {
        var now = time.GetUtcNow();
        db.RefusedAttempts.Add(new RefusedAttemptEntity
        {
            At = now,
            Endpoint = endpoint,
            IpAddress = Truncate(ip, 64),
            KeyHint = keyHint,
            Reason = error.ToString(),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogRefused(endpoint, error, keyHint ?? "-", ip ?? "-");
        return LicenseServerResponse.Fail(error, now, transfersRemaining);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refus {Endpoint} : {Error} (clé {KeyHint}, IP {Ip})")]
    private partial void LogRefused(string endpoint, LicenseErrorCode error, string keyHint, string ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transfert de la licence {KeyHint} : {From} -> {To}")]
    private partial void LogTransfer(string keyHint, Guid from, Guid to);
}
