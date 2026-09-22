using PcSante.Core.Windows;
using PcSante.Ipc.Security;

namespace PcSante.Ipc.Tests;

public class ClientTrustPolicyTests
{
    private const string InstallDir = "/opt/PcSante";
    private static readonly SignatureInfo ServiceSig = new(true, true, "PC Santé", "THUMB-1");
    private static readonly SignatureInfo SameSigner = new(true, true, "PC Santé", "thumb-1");
    private static readonly SignatureInfo OtherSigner = new(true, true, "Autre éditeur", "THUMB-2");

    private static ClientTrustPolicy Policy(bool requireSignature = true) =>
        new(InstallDir, ClientTrustPolicy.ApplicationExecutables, requireSignature);

    private static ClientProcessFacts Client(string? path) => new(1234, path, "alice", "S-1-5-21-1");

    [Fact]
    public void Client_signe_de_l_application_accepte()
    {
        Policy().Evaluate(Client($"{InstallDir}/PcSante.exe"), SameSigner, ServiceSig).Should().Be(TrustDecision.Trusted);
    }

    [Fact]
    public void Client_non_signe_refuse()
    {
        Policy().Evaluate(Client($"{InstallDir}/PcSante.exe"), SignatureInfo.Unsigned, ServiceSig).Should().Be(TrustDecision.Unsigned);
        Policy().Evaluate(Client($"{InstallDir}/PcSante.exe"), new SignatureInfo(true, false, "x", "THUMB-1"), ServiceSig)
            .Should().Be(TrustDecision.Unsigned, "une signature non approuvée ne vaut pas signature");
    }

    [Fact]
    public void Client_signe_par_un_autre_editeur_refuse()
    {
        Policy().Evaluate(Client($"{InstallDir}/PcSante.exe"), OtherSigner, ServiceSig).Should().Be(TrustDecision.SignerMismatch);
    }

    [Fact]
    public void Service_non_signe_aucun_client_accepte_en_release()
    {
        Policy().Evaluate(Client($"{InstallDir}/PcSante.exe"), SameSigner, SignatureInfo.Unsigned).Should().Be(TrustDecision.SignerMismatch);
    }

    [Theory]
    [InlineData("/tmp/PcSante.exe")]
    [InlineData("/opt/PcSante/sub/PcSante.exe")]
    [InlineData("/opt/PcSanteFake/PcSante.exe")]
    public void Client_hors_du_dossier_d_installation_refuse(string path)
    {
        Policy().Evaluate(Client(path), SameSigner, ServiceSig).Should().Be(TrustDecision.OutsideInstallDirectory);
    }

    [Fact]
    public void Autre_executable_du_dossier_refuse()
    {
        Policy().Evaluate(Client($"{InstallDir}/powershell.exe"), SameSigner, ServiceSig).Should().Be(TrustDecision.NotAnApplicationBinary);
    }

    [Fact]
    public void Processus_inconnu_refuse()
    {
        Policy().Evaluate(Client(null), SameSigner, ServiceSig).Should().Be(TrustDecision.UnknownProcess);
        Policy().Evaluate(Client("\0"), SameSigner, ServiceSig).Should().Be(TrustDecision.UnknownProcess);
    }

    [Fact]
    public void Mode_developpement_signature_facultative_mais_dossier_exige()
    {
        Policy(requireSignature: false).Evaluate(Client($"{InstallDir}/PcSante.Overlay.exe"), SignatureInfo.Unsigned, SignatureInfo.Unsigned)
            .Should().Be(TrustDecision.Trusted);
        Policy(requireSignature: false).Evaluate(Client("/tmp/PcSante.exe"), SignatureInfo.Unsigned, SignatureInfo.Unsigned)
            .Should().Be(TrustDecision.OutsideInstallDirectory);
    }

    [Fact]
    public void Verificateur_combine_inspection_signature_et_politique()
    {
        var signatures = new FakeSignatures { [$"{InstallDir}/PcSante.exe"] = SameSigner, [$"{InstallDir}/PcSante.Service.exe"] = ServiceSig };
        var verifier = new ClientVerifier(new FakeInspector(Client($"{InstallDir}/PcSante.exe")), signatures, Policy(), $"{InstallDir}/PcSante.Service.exe");

        var ok = verifier.Verify(null!);
        ok.IsTrusted.Should().BeTrue();
        ok.Client!.UserName.Should().Be("alice");
        ok.Client.ProcessId.Should().Be(1234);

        var refused = new ClientVerifier(new FakeInspector(Client("/tmp/evil.exe")), signatures, Policy(), $"{InstallDir}/PcSante.Service.exe").Verify(null!);
        refused.IsTrusted.Should().BeFalse();
        refused.Client.Should().BeNull();

        new ClientVerifier(new FakeInspector(Client(null)), signatures, Policy(), $"{InstallDir}/PcSante.Service.exe").Verify(null!)
            .Decision.Should().Be(TrustDecision.UnknownProcess);
    }
}

internal sealed class FakeSignatures : Dictionary<string, SignatureInfo>, ISignatureVerifier
{
    public SignatureInfo Verify(string filePath) => TryGetValue(filePath, out var s) ? s : SignatureInfo.Unsigned;
}

internal sealed class FakeInspector(ClientProcessFacts facts) : IClientProcessInspector
{
    public ClientProcessFacts Inspect(System.IO.Pipes.NamedPipeServerStream pipe) => facts;
}
