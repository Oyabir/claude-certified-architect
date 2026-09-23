using PcSante.Core.Licensing;

namespace PcSante.Licensing.Tests;

internal sealed class FakeHardware(HardwareIdentity identity) : IHardwareInfoProvider
{
    public HardwareIdentity Identity { get; set; } = identity;

    public HardwareIdentity Read() => Identity;

    public static HardwareIdentity Pc1 => new("BOARD-111", "DISK-111", "CPU-111", "guid-111");

    public static HardwareIdentity Pc2 => new("BOARD-222", "DISK-222", "CPU-222", "guid-222");
}

/// <summary>Protecteur simulant DPAPI machine : un fichier chiffré sur un PC n'est pas lisible sur un autre.</summary>
internal sealed class FakeMachineProtector(byte machine) : ISecretProtector
{
    public byte[] Protect(byte[] data) => [machine, .. data.Select(b => (byte)(b ^ machine))];

    public byte[]? Unprotect(byte[] data) =>
        data.Length == 0 || data[0] != machine ? null : data.Skip(1).Select(b => (byte)(b ^ machine)).ToArray();
}

/// <summary>Serveur de licences simulé en mémoire (règles simplifiées), signé avec une vraie clé Ed25519.</summary>
internal sealed class FakeLicenseServer(byte[] privateKey, TimeProvider time) : ILicenseServerClient
{
    public bool Online { get; set; } = true;

    public LicenseErrorCode? ForcedError { get; set; }

    public LicenseTier Tier { get; set; } = LicenseTier.Premium;

    public DateTimeOffset? LicenseExpiresAt { get; set; }

    public Dictionary<Guid, IReadOnlyList<string>> Activations { get; } = [];

    public string? TamperToken { get; set; }

    public IReadOnlyList<string>? ForcedFingerprint { get; set; }

    public int Calls { get; private set; }

    public Task<LicenseServerResponse?> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken) =>
        Respond(LicenseKeyFormat.Hash(request.LicenseKey), Guid.NewGuid(), request.Fingerprint);

    public Task<LicenseServerResponse?> RevalidateAsync(RevalidationRequest request, CancellationToken cancellationToken) =>
        Respond(request.KeyHash, request.ActivationId, request.Fingerprint);

    public Task<LicenseServerResponse?> TransferAsync(TransferRequest request, CancellationToken cancellationToken) =>
        Respond(LicenseKeyFormat.Hash(request.LicenseKey), Guid.NewGuid(), request.Fingerprint, transfersRemaining: 1);

    public Task<LicenseServerResponse?> DeactivateAsync(DeactivationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        if (!Online)
        {
            return Task.FromResult<LicenseServerResponse?>(null);
        }

        return Task.FromResult<LicenseServerResponse?>(ForcedError is { } e
            ? LicenseServerResponse.Fail(e, time.GetUtcNow())
            : new LicenseServerResponse { Ok = true, ServerTime = time.GetUtcNow() });
    }

    public Task<SignedUpdateManifest?> GetLatestUpdateAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SignedUpdateManifest?>(null);

    private Task<LicenseServerResponse?> Respond(string keyHash, Guid activationId, IReadOnlyList<string> fingerprint, int? transfersRemaining = null)
    {
        Calls++;
        if (!Online)
        {
            return Task.FromResult<LicenseServerResponse?>(null);
        }

        var now = time.GetUtcNow();
        if (ForcedError is { } error)
        {
            return Task.FromResult<LicenseServerResponse?>(LicenseServerResponse.Fail(error, now));
        }

        Activations[activationId] = fingerprint;
        var token = LicenseTokenCodec.Sign(new LicenseTokenPayload
        {
            KeyHash = keyHash,
            KeyHint = "…TEST",
            ActivationId = activationId,
            Fingerprint = ForcedFingerprint ?? fingerprint,
            Tier = Tier,
            IssuedAt = now,
            ExpiresAt = now + LicenseTokenCodec.TokenLifetime,
            LicenseExpiresAt = LicenseExpiresAt,
        }, privateKey);
        return Task.FromResult<LicenseServerResponse?>(LicenseServerResponse.Success(TamperToken ?? token, now, transfersRemaining));
    }
}

/// <summary>Horloge réglable dans les deux sens (simule un utilisateur qui recule la date du PC).</summary>
internal sealed class AdjustableTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;

    public void SetUtcNow(DateTimeOffset value) => Now = value;
}
