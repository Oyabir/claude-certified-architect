using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Licensing;
using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Ipc;
using PcSante.Ipc.Security;
using PcSante.Licensing;
using PcSante.Service.Dispatch;
using PcSante.Service.Updates;

namespace PcSante.Service.Tests;

/// <summary>Service complet avec simulations Windows et base SQLite temporaire.</summary>
internal sealed class ServiceFixture : IAsyncDisposable
{
    public ServiceFixture(bool premium = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "pcsante-svc-" + Guid.NewGuid().ToString("N"));
        Paths = new ServicePaths(Path.Combine(root, "data"), Path.Combine(root, "install"), Path.Combine(root, "reports"));
        Paths.EnsureCreated();
        Directory.CreateDirectory(Paths.InstallDirectory);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddPcSanteCore(Paths);
        services.AddSingleton<IDefenderApi>(Defender);
        services.AddSingleton<IFirewallApi>(Firewall);
        services.AddSingleton<IWindowsUpdateApi>(Updates);
        services.AddSingleton<ISystemRepairApi>(Repair);
        services.AddSingleton<INetworkRepairApi>(Network);
        services.AddSingleton<ILocalAccountsApi>(Accounts);
        services.AddSingleton<ISecurityCenterApi>(SecurityCenter);
        services.AddSingleton<IRestorePointApi>(Restore);
        services.AddSingleton<IProcessApi>(Processes);
        services.AddSingleton<IServiceControlApi>(ServicesApi);
        services.AddSingleton<ISignatureVerifier>(Signatures);
        services.AddSingleton<IStartupApi>(Startup);
        services.AddSingleton<IThirdPartyTaskApi>(Tasks);
        services.AddSingleton<ICleanupApi>(Cleanup);
        services.AddSingleton<IPowerApi>(Power);
        services.AddSingleton<IMetricsProvider>(Metrics);
        services.AddSingleton<ISystemInfoApi>(System);
        services.AddSingleton<IScheduledTemplateApi>(Scheduler);
        services.AddSingleton<IHardwareInfoProvider>(new FakeHardware());
        services.AddSingleton<ISecretProtector>(new PlainProtector());
        services.AddSingleton<IInstallerLauncher>(Launcher);
        services.AddSingleton<ILicenseServerClient>(LicenseServer);
        services.AddSingleton(sp => new LicenseManager(LicenseServer, sp.GetRequiredService<ILicenseStateStore>(), new FakeHardware(),
            TimeProvider.System, Convert.ToBase64String(LicenseServer.PublicKey)));
        services.AddSingleton<IPipeStreamFactory, UnsecuredPipeStreamFactory>();
        services.AddSingleton<IClientVerifier>(_ => Verifier);
        Provider = services.BuildServiceProvider();
        ServiceRegistration.EnsureDatabase(Provider);

        if (premium)
        {
            License.ActivateAsync(LicenseKeyFormat.Generate(), default).GetAwaiter().GetResult().Success.Should().BeTrue();
        }
    }

    public ServicePaths Paths { get; }

    public ServiceProvider Provider { get; }

    public FakeDefender Defender { get; } = new();

    public FakeFirewall Firewall { get; } = new();

    public FakeUpdates Updates { get; } = new();

    public FakeRepair Repair { get; } = new();

    public FakeNetwork Network { get; } = new();

    public FakeAccounts Accounts { get; } = new();

    public FakeSecurityCenter SecurityCenter { get; } = new();

    public FakeRestore Restore { get; } = new();

    public FakeProcesses Processes { get; } = new();

    public FakeServices ServicesApi { get; } = new();

    public FakeSignatures Signatures { get; } = new();

    public FakeStartup Startup { get; } = new();

    public FakeTasks Tasks { get; } = new();

    public FakeCleanup Cleanup { get; } = new();

    public FakePower Power { get; } = new();

    public FakeMetrics Metrics { get; } = new();

    public FakeSystem System { get; } = new();

    public FakeScheduler Scheduler { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public FakeLicenseServer LicenseServer { get; } = new();

    public IClientVerifier Verifier { get; set; } = new TrustedVerifier();

    public CommandDispatcher Dispatcher => Provider.GetRequiredService<CommandDispatcher>();

    public LicenseManager License => Provider.GetRequiredService<LicenseManager>();

    public static CallerIdentity Alice { get; } = new("PC\\alice", "S-1-5-21-1", 4242);

    public Task<CommandResult> Run(CommandId command, Dictionary<string, string>? parameters = null, bool confirmed = false) =>
        Dispatcher.DispatchAsync(command.ToString(), parameters, confirmed, Alice, default);

    public Task<CommandResult> RunRaw(string command, Dictionary<string, string>? parameters = null, bool confirmed = false) =>
        Dispatcher.DispatchAsync(command, parameters, confirmed, Alice, default);

    public async Task<IReadOnlyList<Core.Audit.AuditEntry>> AuditAsync() =>
        await Provider.GetRequiredService<Core.Audit.IAuditLog>().ReadAsync(DateTimeOffset.MinValue, 1000).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync().ConfigureAwait(false);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path.GetDirectoryName(Paths.DataRoot)!, true);
        }
        catch (IOException)
        {
            // Nettoyage best effort.
        }
    }
}

internal sealed class TrustedVerifier : IClientVerifier
{
    public ClientVerification Verify(System.IO.Pipes.NamedPipeServerStream pipe) =>
        new(true, new ClientInfo(4242, "/install/PcSante.exe", "PC\\alice", "S-1-5-21-1"), TrustDecision.Trusted, null);
}
