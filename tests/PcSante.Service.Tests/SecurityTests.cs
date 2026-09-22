using System.IO.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PcSante.Core;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Windows;
using PcSante.Ipc;
using PcSante.Ipc.Security;
using PcSante.Service.Dispatch;

namespace PcSante.Service.Tests;

/// <summary>Tests de sécurité exigés par la section 13.</summary>
public sealed class SecurityTests
{
    [Theory]
    [InlineData("FormatDisk")]
    [InlineData("RunScript")]
    [InlineData("RemoveQuarantinedItem")]
    [InlineData("powershell -c whoami")]
    [InlineData("100")]
    public async Task Commande_hors_catalogue_refusee_et_journalisee(string command)
    {
        await using var svc = new ServiceFixture();

        var result = await svc.RunRaw(command);

        result.Status.Should().Be(CommandStatus.Refused);
        result.Reason.Should().Be(FailureReason.NotInCatalog);
        result.MessageKey.Should().Be("Result_NotInCatalog");
        var audit = await svc.AuditAsync();
        audit.Should().Contain(a => a.Outcome == AuditOutcome.Refused && a.Reason == "NotInCatalog" && a.Who == "PC\\alice");
    }

    [Fact]
    public async Task Parametre_etranger_refuse()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.EnableRealtimeProtection, new() { ["script"] = "calc.exe" });

        result.Reason.Should().Be(FailureReason.InvalidParameters);
        svc.Defender.Status.RealTimeProtectionEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Action_destructive_sans_confirmation_refusee()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.CleanTemporaryFiles);

        result.Reason.Should().Be(FailureReason.ConfirmationRequired);
        svc.Cleanup.Cleaned.Should().BeEmpty();
    }

    [Fact]
    public async Task Offre_gratuite_actions_premium_refusees_diagnostic_autorise()
    {
        await using var svc = new ServiceFixture(premium: false);

        (await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Public" })).Reason.Should().Be(FailureReason.LicenseRequired);
        svc.Firewall.Profiles[FirewallProfile.Public].Should().BeFalse();
        (await svc.Run(CommandId.RunHealthAnalysis)).IsSuccess.Should().BeTrue();
        (await svc.Run(CommandId.GetLiveMetrics)).IsSuccess.Should().BeTrue();
        (await svc.Run(CommandId.GetLicenseStatus)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Client_non_signe_refuse_par_le_named_pipe_du_service()
    {
        await using var svc = new ServiceFixture();
        const string install = "/opt/PcSante";
        var signatures = new FakeSignatures();
        signatures.Known[$"{install}/PcSante.Service.exe"] = new SignatureInfo(true, true, "PC Santé", "AAA");
        svc.Verifier = new ClientVerifier(
            new FixedInspector(new ClientProcessFacts(999, $"{install}/PcSante.exe", "PC\\mallory", "S-1-5-21-9")),
            signatures,
            new ClientTrustPolicy(install, ClientTrustPolicy.ApplicationExecutables, requireSignature: true),
            $"{install}/PcSante.Service.exe");
        var pipeName = "pcsante-svc-test-" + Guid.NewGuid().ToString("N")[..10];
        var server = new PipeServer(pipeName, new UnsecuredPipeStreamFactory(), svc.Verifier, svc.Dispatcher, NullLogger<PipeServer>.Instance);
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(cts.Token);

        await using (var client = new PipeClient(pipeName, new AcceptAnyServer()))
        {
            var result = await client.SendAsync(CommandId.EnableFirewallProfile, new Dictionary<string, string> { ["profile"] = "Public" });
            result.Reason.Should().Be(FailureReason.ClientNotTrusted);
        }

        svc.Firewall.Profiles[FirewallProfile.Public].Should().BeFalse("aucune commande d'un client non signé n'est exécutée");

        // Le même client, signé par le même certificat que le service, est accepté.
        signatures.Known[$"{install}/PcSante.exe"] = new SignatureInfo(true, true, "PC Santé", "AAA");
        svc.Verifier = new ClientVerifier(
            new FixedInspector(new ClientProcessFacts(999, $"{install}/PcSante.exe", "PC\\alice", "S-1-5-21-1")),
            signatures,
            new ClientTrustPolicy(install, ClientTrustPolicy.ApplicationExecutables, requireSignature: true),
            $"{install}/PcSante.Service.exe");
        var server2Name = pipeName + "b";
        var server2 = new PipeServer(server2Name, new UnsecuredPipeStreamFactory(), svc.Verifier, svc.Dispatcher, NullLogger<PipeServer>.Instance);
        var run2 = server2.RunAsync(cts.Token);
        await using (var client = new PipeClient(server2Name, new AcceptAnyServer()))
        {
            (await client.SendAsync(CommandId.EnableFirewallProfile, new Dictionary<string, string> { ["profile"] = "Public" })).Status
                .Should().Be(CommandStatus.Succeeded);
        }

        await cts.CancelAsync();
        await Task.WhenAll(run, run2);
        (await svc.AuditAsync()).Should().Contain(a => a.Command == "EnableFirewallProfile" && a.Who == "PC\\alice" && a.ClientProcessId == 999);
    }

    [Fact]
    public async Task Cle_de_licence_jamais_en_clair_dans_l_audit()
    {
        await using var svc = new ServiceFixture(premium: false);
        var key = Core.Licensing.LicenseKeyFormat.Generate();

        (await svc.Run(CommandId.ActivateLicense, new() { ["key"] = key })).IsSuccess.Should().BeTrue();

        var entry = (await svc.AuditAsync()).Single(a => a.Command == "ActivateLicense");
        entry.Parameters.Should().NotContain(key[4..14]);
        entry.Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [Fact]
    public void Catalogue_complet_et_ferme()
    {
        using var provider = new ServiceFixture().Provider;
        var catalog = provider.GetRequiredService<CommandCatalog>();

        catalog.Commands.Should().BeEquivalentTo(CommandDefinitions.All.Keys);
    }

    [Fact]
    public void Gestionnaire_hors_catalogue_rejete_au_demarrage()
    {
        var bogus = new DelegateQuery((CommandId)12345, (_, _, _) => Task.FromResult(CommandResult.Success("x")));
        var act = () => new CommandCatalog([bogus], [], []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*hors catalogue*");
        var incomplete = () => new CommandCatalog([], [], []);
        incomplete.Should().Throw<InvalidOperationException>().WithMessage("*sans gestionnaire*");
    }

    [Fact]
    public void Ipc_utilise_le_nom_de_pipe_versionne()
    {
        ProductInfo.PipeName.Should().EndWith(".v1");
    }

    private sealed class FixedInspector(ClientProcessFacts facts) : IClientProcessInspector
    {
        public ClientProcessFacts Inspect(NamedPipeServerStream pipe) => facts;
    }
}
