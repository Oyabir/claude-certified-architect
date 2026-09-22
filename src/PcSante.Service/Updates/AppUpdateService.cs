using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Updates;
using PcSante.Core.Windows;
using PcSante.Licensing;

namespace PcSante.Service.Updates;

/// <summary>Lancement de l'installeur (msiexec). Abstrait pour les tests.</summary>
public interface IInstallerLauncher
{
    bool Launch(string msiPath);
}

/// <summary>
/// Mises à jour signées et vérifiées avant installation (section 5) :
/// manifeste signé Ed25519 par le serveur, empreinte SHA-256 du MSI, signature Authenticode
/// du MSI émise par le même certificat que le service.
/// </summary>
public sealed class AppUpdateService(
    ILicenseServerClient server,
    ISignatureVerifier signatures,
    IHttpClientFactory http,
    IInstallerLauncher launcher,
    ServicePaths paths)
{
    private readonly byte[]? _publicKey = Decode(ProductInfo.LicensePublicKey);

    public byte[]? PublicKeyOverride { get; init; }

    private byte[]? PublicKey => PublicKeyOverride ?? _publicKey;

    public async Task<(AppUpdateInfo Info, UpdateManifest? Manifest)> CheckAsync(CancellationToken cancellationToken)
    {
        var current = ProductInfo.Version;
        var signed = await server.GetLatestUpdateAsync(cancellationToken).ConfigureAwait(false);
        var manifest = VerifyManifest(signed);
        if (manifest is null || !Version.TryParse(manifest.Version, out var remote) || !Version.TryParse(current, out var local) || remote <= local)
        {
            return (new AppUpdateInfo(false, current, null), null);
        }

        return (new AppUpdateInfo(true, current, manifest.Version), manifest);
    }

    public UpdateManifest? VerifyManifest(SignedUpdateManifest? signed)
    {
        if (signed is null || PublicKey is null)
        {
            return null;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(signed.Manifest);
            if (!Ed25519.Verify(PublicKey, bytes, Convert.FromBase64String(signed.Signature)))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(signed.Manifest, PcSanteJson.Options);
            return manifest is { DownloadUrl.Scheme: "https", Sha256.Length: 64 } ? manifest : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public async Task<CommandResult> InstallAsync(CancellationToken cancellationToken)
    {
        var (info, manifest) = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!info.Available || manifest is null)
        {
            return new CommandResult { Status = CommandStatus.AlreadyDone, MessageKey = "Result_AppUpToDate" };
        }

        Directory.CreateDirectory(paths.Updates);
        var file = Path.Combine(paths.Updates, string.Create(CultureInfo.InvariantCulture, $"PcSante-{manifest.Version}.msi"));
        try
        {
            using var client = http.CreateClient("updates");
            await using (var source = await client.GetStreamAsync(manifest.DownloadUrl, cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(file))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return CommandResult.Failure(FailureReason.NetworkUnavailable, "Result_UpdateDownloadFailed", ex.Message);
        }

        if (!await VerifyFileAsync(file, manifest.Sha256, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(file);
            return CommandResult.Failure(FailureReason.VerificationFailed, "Result_UpdateSignatureInvalid");
        }

        return launcher.Launch(file)
            ? CommandResult.Success("Result_UpdateInstalling", manifest.Version)
            : CommandResult.Failure(FailureReason.ExecutionFailed, "Result_UpdateLaunchFailed");
    }

    /// <summary>Empreinte SHA-256 conforme ET signature Authenticode du même éditeur que le service.</summary>
    public async Task<bool> VerifyFileAsync(string file, string expectedSha256, CancellationToken cancellationToken)
    {
        await using (var stream = File.OpenRead(file))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var msi = signatures.Verify(file);
        var service = signatures.Verify(paths.ServiceExecutable);
        return msi is { IsSigned: true, IsTrusted: true } && service.IsSigned
            && string.Equals(msi.Thumbprint, service.Thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? Decode(string base64)
    {
        try
        {
            return string.IsNullOrWhiteSpace(base64) ? null : Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
