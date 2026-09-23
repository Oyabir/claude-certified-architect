using PcSante.Core.Licensing;
using PcSante.Licensing;

namespace PcSante.Service.Tests;

/// <summary>Serveur de licences simulé : signe de vrais jetons avec une clé de test.</summary>
internal sealed class FakeLicenseServer : ILicenseServerClient
{
    public FakeLicenseServer()
    {
        (PrivateKey, PublicKey) = Ed25519.GenerateKeyPair();
    }

    public byte[] PrivateKey { get; }

    public byte[] PublicKey { get; }

    public SignedUpdateManifest? Update { get; set; }

    public Task<LicenseServerResponse?> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<LicenseServerResponse?>(Token(LicenseKeyFormat.Hash(request.LicenseKey), request.Fingerprint));

    public Task<LicenseServerResponse?> RevalidateAsync(RevalidationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<LicenseServerResponse?>(Token(request.KeyHash, request.Fingerprint));

    public Task<LicenseServerResponse?> TransferAsync(TransferRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<LicenseServerResponse?>(Token(LicenseKeyFormat.Hash(request.LicenseKey), request.Fingerprint));

    public Task<LicenseServerResponse?> DeactivateAsync(DeactivationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<LicenseServerResponse?>(new LicenseServerResponse { Ok = true, ServerTime = DateTimeOffset.UtcNow });

    public Task<SignedUpdateManifest?> GetLatestUpdateAsync(CancellationToken cancellationToken) => Task.FromResult(Update);

    private LicenseServerResponse Token(string keyHash, IReadOnlyList<string> fingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        return LicenseServerResponse.Success(LicenseTokenCodec.Sign(new LicenseTokenPayload
        {
            KeyHash = keyHash,
            KeyHint = "…TEST",
            ActivationId = Guid.NewGuid(),
            Fingerprint = fingerprint,
            Tier = LicenseTier.Premium,
            IssuedAt = now,
            ExpiresAt = now.AddDays(14),
        }, PrivateKey), now);
    }
}
