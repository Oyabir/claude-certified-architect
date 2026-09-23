using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Windows;
using PcSante.Licensing;
using PcSante.Service.Updates;

namespace PcSante.Service.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private readonly FakeLicenseServer _server = new();
    private readonly FakeSignatures _signatures = new();
    private readonly FakeLauncher _launcher = new();
    private readonly ServicePaths _paths;
    private readonly byte[] _msi = Encoding.UTF8.GetBytes("faux MSI");

    public AppUpdateTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcsante-upd-" + Guid.NewGuid().ToString("N"));
        _paths = new ServicePaths(Path.Combine(root, "data"), Path.Combine(root, "install"));
        _paths.EnsureCreated();
        _signatures.Known[_paths.ServiceExecutable] = new SignatureInfo(true, true, "PC Santé", "CERT");
    }

    private AppUpdateService Service() => new(_server, _signatures, new StaticHttpFactory(_msi), _launcher, _paths) { PublicKeyOverride = _server.PublicKey };

    private SignedUpdateManifest Sign(string version, string? sha = null, byte[]? key = null)
    {
        var manifest = JsonSerializer.Serialize(new UpdateManifest(version, new Uri("https://example.invalid/u.msi"),
            sha ?? Convert.ToHexString(SHA256.HashData(_msi)), DateTimeOffset.UtcNow), PcSanteJson.Options);
        return new SignedUpdateManifest(manifest, Convert.ToBase64String(Ed25519.Sign(key ?? _server.PrivateKey, Encoding.UTF8.GetBytes(manifest))));
    }

    [Fact]
    public async Task Aucune_mise_a_jour()
    {
        (await Service().CheckAsync(default)).Info.Available.Should().BeFalse();
        _server.Update = Sign("0.9.0");
        (await Service().CheckAsync(default)).Info.Available.Should().BeFalse();
        (await Service().InstallAsync(default)).Status.Should().Be(CommandStatus.AlreadyDone);
    }

    [Fact]
    public async Task Manifeste_non_signe_par_le_serveur_ignore()
    {
        _server.Update = Sign("9.0.0", key: Ed25519.GenerateKeyPair().PrivateKey);

        (await Service().CheckAsync(default)).Info.Available.Should().BeFalse();
        Service().VerifyManifest(new SignedUpdateManifest("{}", "pas-du-base64")).Should().BeNull();
    }

    [Fact]
    public async Task Mise_a_jour_signee_installee()
    {
        _server.Update = Sign("9.0.0");
        _signatures.Known[Path.Combine(_paths.Updates, "PcSante-9.0.0.msi")] = new SignatureInfo(true, true, "PC Santé", "CERT");

        var info = (await Service().CheckAsync(default)).Info;
        info.Available.Should().BeTrue();
        info.NewVersion.Should().Be("9.0.0");

        var result = await Service().InstallAsync(default);
        result.MessageKey.Should().Be("Result_UpdateInstalling");
        _launcher.Launched.Should().ContainSingle();
    }

    [Fact]
    public async Task MSI_signe_par_un_autre_editeur_refuse()
    {
        _server.Update = Sign("9.0.0");
        _signatures.Known[Path.Combine(_paths.Updates, "PcSante-9.0.0.msi")] = new SignatureInfo(true, true, "Pirate", "OTHER");

        var result = await Service().InstallAsync(default);

        result.MessageKey.Should().Be("Result_UpdateSignatureInvalid");
        _launcher.Launched.Should().BeEmpty();
        File.Exists(Path.Combine(_paths.Updates, "PcSante-9.0.0.msi")).Should().BeFalse();
    }

    [Fact]
    public async Task MSI_altere_refuse()
    {
        _server.Update = Sign("9.0.0", sha: new string('0', 64));

        (await Service().InstallAsync(default)).Reason.Should().Be(FailureReason.VerificationFailed);
        _launcher.Launched.Should().BeEmpty();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_paths.DataRoot)!, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private sealed class StaticHttpFactory(byte[] content) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StaticHandler(content));
    }

    private sealed class StaticHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }
}
