using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Licensing;
using PcSante.Service.Updates;

namespace PcSante.Service.Tests;

internal sealed class FakeDefender : IDefenderApi
{
    public DefenderStatus Status { get; set; } = new()
    {
        IsAvailable = true,
        IsActiveAntivirus = true,
        RealTimeProtectionEnabled = true,
        CloudProtectionEnabled = true,
        SignaturesUpdatedAt = DateTimeOffset.UtcNow,
        LastQuickScanAt = DateTimeOffset.UtcNow,
    };

    public bool RefuseChanges { get; set; }

    public TaskCompletionSource<bool> ScanGate { get; } = new();

    public List<(DefenderScanType Type, string? Path)> Scans { get; } = [];

    public List<QuarantineItem> Quarantine { get; } = [new("Virus:DOS/EICAR", "Virus:DOS/EICAR", DateTimeOffset.UtcNow, @"C:\x.txt")];

    public List<ThreatInfo> Threats { get; } = [new("1", "Virus:DOS/EICAR", 5, DateTimeOffset.UtcNow.AddDays(-1), "Handled", @"C:\x.txt")];

    public Task<DefenderStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<DefenderPreferences> GetPreferencesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DefenderPreferences(Status.RealTimeProtectionEnabled, Status.CloudProtectionEnabled ? 2 : 0, Status.ControlledFolderAccessEnabled ? 1 : 0));

    public Task<bool> SetRealTimeProtectionAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!RefuseChanges)
        {
            Status = Status with { RealTimeProtectionEnabled = enabled };
        }

        return Task.FromResult(true);
    }

    public Task<bool> SetCloudReportingLevelAsync(int level, CancellationToken cancellationToken)
    {
        Status = Status with { CloudProtectionEnabled = level > 0 };
        return Task.FromResult(true);
    }

    public Task<bool> SetControlledFolderAccessAsync(int mode, CancellationToken cancellationToken)
    {
        Status = Status with { ControlledFolderAccessEnabled = mode == 1 };
        return Task.FromResult(true);
    }

    public async Task<bool> RunScanAsync(DefenderScanType type, string? path, CancellationToken cancellationToken)
    {
        Scans.Add((type, path));
        return await ScanGate.Task.ConfigureAwait(false);
    }

    public Task<bool> UpdateSignaturesAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<ThreatInfo>> GetThreatHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ThreatInfo>>(Threats);

    public Task<IReadOnlyList<QuarantineItem>> GetQuarantineAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<QuarantineItem>>(Quarantine.ToList());

    public Task<bool> RestoreFromQuarantineAsync(string id, CancellationToken cancellationToken)
    {
        Quarantine.RemoveAll(q => q.Id == id);
        return Task.FromResult(true);
    }
}

internal sealed class FakeFirewall : IFirewallApi
{
    public Dictionary<FirewallProfile, bool> Profiles { get; } = new()
    {
        [FirewallProfile.Domain] = true,
        [FirewallProfile.Private] = true,
        [FirewallProfile.Public] = false,
    };

    public bool IgnoreWrites { get; set; }

    public List<string> Exports { get; } = [];

    public Task<IReadOnlyList<FirewallProfileStatus>> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FirewallProfileStatus>>(Profiles.Select(p => new FirewallProfileStatus(p.Key, p.Value)).ToList());

    public Task<bool> SetProfileEnabledAsync(FirewallProfile profile, bool enabled, CancellationToken cancellationToken)
    {
        if (!IgnoreWrites)
        {
            Profiles[profile] = enabled;
        }

        return Task.FromResult(true);
    }

    public Task<bool> ExportPolicyAsync(string filePath, CancellationToken cancellationToken)
    {
        File.WriteAllText(filePath, "export");
        Exports.Add(filePath);
        return Task.FromResult(true);
    }

    public Task<bool> ImportPolicyAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> ResetToDefaultAsync(CancellationToken cancellationToken)
    {
        foreach (var key in Profiles.Keys.ToList())
        {
            Profiles[key] = true;
        }

        return Task.FromResult(true);
    }
}

internal sealed class FakeUpdates : IWindowsUpdateApi
{
    public List<PendingUpdate> Pending { get; } = [new("u1", "Mise à jour cumulative", true, 1000)];

    public string? Backup { get; private set; }

    public Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateStatus(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, true));

    public Task<IReadOnlyList<PendingUpdate>> SearchAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PendingUpdate>>(Pending.ToList());

    public Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        var n = Pending.Count;
        Pending.Clear();
        return Task.FromResult(new UpdateInstallResult(n, 0, true));
    }

    public Task<bool> ResetComponentsAsync(string backupPath, CancellationToken cancellationToken)
    {
        Backup = backupPath;
        return Task.FromResult(true);
    }

    public string ProposeBackupPath(DateTimeOffset now) => $@"C:\Windows\SoftwareDistribution.pcsante-{now:yyyyMMddHHmmss}";

    public Task<bool> RestoreComponentsAsync(string backupPath, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> AreServicesHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class FakeRepair : ISystemRepairApi
{
    public RepairOutcome Outcome { get; set; } = RepairOutcome.NoProblemFound;

    public Task<RepairResult> RunSystemFileCheckAsync(CancellationToken cancellationToken) => Task.FromResult(new RepairResult(Outcome, 0, "sfc"));

    public Task<RepairResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new RepairResult(Outcome, 0, "dism"));
}

internal sealed class FakeRestore : IRestorePointApi
{
    public bool Enabled { get; set; } = true;

    public List<RestorePointInfo> Points { get; } = [];

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(Enabled);

    public Task<bool> EnableAsync(CancellationToken cancellationToken)
    {
        Enabled = true;
        return Task.FromResult(true);
    }

    public Task<bool> CreateAsync(string description, CancellationToken cancellationToken)
    {
        if (Enabled)
        {
            Points.Add(new RestorePointInfo(Points.Count + 1, description, DateTimeOffset.UtcNow));
        }

        return Task.FromResult(Enabled);
    }

    public Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RestorePointInfo>>(Points.ToList());
}

internal sealed class FakeProcesses : IProcessApi
{
    public List<ProcessSample> Running { get; } =
    [
        new(100, "chrome", @"C:\Program Files\Google\chrome.exe", 25, 800L << 20, 0, []),
        new(4, "lsass", @"C:\Windows\System32\lsass.exe", 1, 20L << 20, 0, []),
        new(200, "updater", @"C:\Users\a\AppData\Local\Temp\updater.exe", 2, 30L << 20, 0, ["EvilUpdater"]),
    ];

    public Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProcessSample>>(Running.ToList());

    public Task<ProcessSample?> GetAsync(int processId, CancellationToken cancellationToken) => Task.FromResult(Running.FirstOrDefault(p => p.ProcessId == processId));

    public Task<bool> StopAsync(int processId, CancellationToken cancellationToken)
    {
        Running.RemoveAll(p => p.ProcessId == processId);
        return Task.FromResult(true);
    }

    public Task<bool> IsRunningAsync(int processId, CancellationToken cancellationToken) => Task.FromResult(Running.Any(p => p.ProcessId == processId));
}

internal sealed class FakeServices : IServiceControlApi
{
    public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EvilUpdater"] = new("EvilUpdater", "Evil Updater", ServiceStartMode.Automatic, true, null),
        ["WinDefend"] = new("WinDefend", "Defender", ServiceStartMode.Automatic, true, null),
    };

    public Task<ServiceInfo?> GetAsync(string serviceName, CancellationToken cancellationToken) => Task.FromResult(Services.GetValueOrDefault(serviceName));

    public Task<bool> SetStartModeAsync(string serviceName, ServiceStartMode mode, CancellationToken cancellationToken)
    {
        Services[serviceName] = Services[serviceName] with { StartMode = mode };
        return Task.FromResult(true);
    }

    public Task<bool> StopAsync(string serviceName, CancellationToken cancellationToken)
    {
        Services[serviceName] = Services[serviceName] with { IsRunning = false };
        return Task.FromResult(true);
    }

    public Task<bool> StartAsync(string serviceName, CancellationToken cancellationToken)
    {
        Services[serviceName] = Services[serviceName] with { IsRunning = true };
        return Task.FromResult(true);
    }
}

internal sealed class FakeSignatures : ISignatureVerifier
{
    public Dictionary<string, SignatureInfo> Known { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SignatureInfo Verify(string filePath) => Known.TryGetValue(filePath, out var s) ? s : SignatureInfo.Unsigned;
}

internal sealed class FakeStartup : IStartupApi
{
    public List<StartupItem> Items { get; } =
    [
        new("MachineRun|OneDrive", "OneDrive", "onedrive.exe", StartupLocation.MachineRun, true, null),
        new("UserRun|Spotify", "Spotify", "spotify.exe", StartupLocation.UserRun, true, null),
    ];

    public string? LastSid { get; private set; }

    public Task<IReadOnlyList<StartupItem>> ListAsync(string? userSid, CancellationToken cancellationToken)
    {
        LastSid = userSid;
        return Task.FromResult<IReadOnlyList<StartupItem>>(Items.ToList());
    }

    public Task<bool> SetEnabledAsync(string id, bool enabled, string? userSid, CancellationToken cancellationToken)
    {
        var i = Items.FindIndex(x => x.Id == id);
        if (i < 0)
        {
            return Task.FromResult(false);
        }

        Items[i] = Items[i] with { Enabled = enabled };
        return Task.FromResult(true);
    }
}

internal sealed class FakeTasks : IThirdPartyTaskApi
{
    public List<ThirdPartyTask> Tasks { get; } = [new(@"\Contoso\Updater", "Updater", "Contoso", true)];

    public Task<IReadOnlyList<ThirdPartyTask>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ThirdPartyTask>>(Tasks.ToList());

    public Task<bool> SetEnabledAsync(string path, bool enabled, CancellationToken cancellationToken)
    {
        var i = Tasks.FindIndex(t => t.Path == path);
        Tasks[i] = Tasks[i] with { Enabled = enabled };
        return Task.FromResult(true);
    }
}

internal sealed class FakeCleanup : ICleanupApi
{
    public CleanupEstimate Estimate { get; set; } = new(500L << 20, 200L << 20, 100L << 20, 50L << 20);

    public List<CleanupTarget> Cleaned { get; } = [];

    public Task<CleanupEstimate> EstimateAsync(CancellationToken cancellationToken) => Task.FromResult(Estimate);

    public Task<CleanupResult> CleanAsync(CleanupTarget target, CancellationToken cancellationToken)
    {
        Cleaned.Add(target);
        var freed = Actions.CleanupAction.BytesFor(Estimate, target);
        Estimate = target switch
        {
            CleanupTarget.TemporaryFiles => Estimate with { TemporaryFilesBytes = 0 },
            CleanupTarget.WindowsUpdateCache => Estimate with { WindowsUpdateCacheBytes = 0 },
            CleanupTarget.RecycleBin => Estimate with { RecycleBinBytes = 0 },
            _ => Estimate with { BrowserCachesBytes = 0 },
        };
        return Task.FromResult(new CleanupResult(freed, 10, 0));
    }
}

internal sealed class FakePower : IPowerApi
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid High = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public Guid Active { get; set; } = Balanced;

    public Task<IReadOnlyList<PowerPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PowerPlan>>([new(Balanced, "Utilisation normale", Active == Balanced), new(High, "Performances élevées", Active == High)]);

    public Task<Guid?> GetActivePlanAsync(CancellationToken cancellationToken) => Task.FromResult<Guid?>(Active);

    public Task<bool> SetActivePlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        Active = planId;
        return Task.FromResult(true);
    }
}

internal sealed class FakeMetrics : IMetricsProvider
{
    public LiveMetrics Sample() => new() { At = DateTimeOffset.UtcNow, CpuPercent = 12, MemoryPercent = 50, MemoryTotalBytes = 8L << 30, MemoryUsedBytes = 4L << 30 };
}

internal sealed class FakeSystem : ISystemInfoApi
{
    public DriveSpace Drive { get; set; } = new("C:", 500L << 30, 200L << 30);

    public SystemInfo GetSystemInfo() => new("Windows 11 Pro", "23H2", 22631, false, "PC-TEST", true);

    public DriveSpace GetSystemDrive() => Drive;

    public int CountCrashesSince(DateTimeOffset since) => 0;

    public Dictionary<int, string> Owners { get; } = [];

    public (string? UserName, string? Sid) GetProcessOwner(int processId) =>
        Owners.TryGetValue(processId, out var sid) ? ("PC\\autre", sid) : ("PC\\alice", "S-1-5-21-1");
}

internal sealed class FakeScheduler : IScheduledTemplateApi
{
    public Dictionary<ScheduledTemplateId, ScheduleSettings> Registered { get; } = [];

    public Task<bool> RegisterAsync(ScheduledTemplateId template, ScheduleSettings settings, CancellationToken cancellationToken)
    {
        Registered[template] = settings;
        return Task.FromResult(true);
    }

    public Task<bool> UnregisterAsync(ScheduledTemplateId template, CancellationToken cancellationToken)
    {
        Registered.Remove(template);
        return Task.FromResult(true);
    }

    public Task<ScheduleSettings?> GetAsync(ScheduledTemplateId template, CancellationToken cancellationToken) =>
        Task.FromResult(Registered.TryGetValue(template, out var s) ? s : null);
}

internal sealed class FakeHardware : IHardwareInfoProvider
{
    public HardwareIdentity Read() => new("BOARD", "DISK", "CPU", "GUID");
}

internal sealed class PlainProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data;

    public byte[]? Unprotect(byte[] data) => data;
}

internal sealed class FakeLauncher : IInstallerLauncher
{
    public List<string> Launched { get; } = [];

    public bool Launch(string msiPath)
    {
        Launched.Add(msiPath);
        return true;
    }
}

internal sealed class FakeNetwork : INetworkRepairApi
{
    public List<string> Calls { get; } = [];

    public bool Succeeds { get; set; } = true;

    public Task<bool> FlushDnsAsync(CancellationToken cancellationToken)
    {
        Calls.Add("dns");
        return Task.FromResult(Succeeds);
    }

    public Task<bool> ResetNetworkStackAsync(CancellationToken cancellationToken)
    {
        Calls.Add("reset");
        return Task.FromResult(Succeeds);
    }
}

internal sealed class FakeAccounts : ILocalAccountsApi
{
    public List<LocalAccount> Accounts { get; } =
    [
        new("alice", "S-1-5-21-1-2-3-1001", true, true),
        new("Invité", "S-1-5-21-1-2-3-501", false, false),
    ];

    public Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocalAccount>>(Accounts.ToList());

    public Task<bool> SetEnabledAsync(string sid, bool enabled, CancellationToken cancellationToken)
    {
        var index = Accounts.FindIndex(a => a.Sid == sid);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Accounts[index] = Accounts[index] with { Enabled = enabled };
        return Task.FromResult(true);
    }

    public Task<bool> IsAdministratorAsync(string? sid, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.Any(a => a.Sid == sid && a.IsAdministrator));
}

internal sealed class FakeSecurityCenter : ISecurityCenterApi
{
    public List<AntivirusProduct> Products { get; } = [new("Windows Defender", true, true, true)];

    public Task<IReadOnlyList<AntivirusProduct>> ListAntivirusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AntivirusProduct>>(Products.ToList());
}

internal sealed class FakeBitLocker : IBitLockerApi
{
    public BitLockerStatus Status { get; set; } = new(true, true, BitLockerState.Off, 0, false);

    public int EncryptionStarts { get; private set; }

    public Task<BitLockerStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<BitLockerRecoveryKey?> EnsureRecoveryKeyAsync(CancellationToken cancellationToken)
    {
        Status = Status with { HasRecoveryKey = true };
        return Task.FromResult<BitLockerRecoveryKey?>(new BitLockerRecoveryKey("{0000-KEY}", "111111-222222-333333-444444-555555-666666-777777-888888"));
    }

    public Task<bool> StartEncryptionAsync(CancellationToken cancellationToken)
    {
        EncryptionStarts++;
        Status = Status with { State = BitLockerState.Encrypting };
        return Task.FromResult(true);
    }
}

internal sealed class FakeSessions : ISessionApi
{
    public List<UserSession> Sessions { get; } =
    [
        new(1, @"PC\alice", SessionState.Active, false, null, DateTimeOffset.UnixEpoch),
        new(2, @"PC\bob", SessionState.Active, true, "203.0.113.7", DateTimeOffset.UnixEpoch),
    ];

    public List<string> Messages { get; } = [];

    public Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSession>>(Sessions.ToList());

    public Task<bool> SendMessageAsync(int sessionId, string title, string message, CancellationToken cancellationToken)
    {
        Messages.Add($"{sessionId}:{message}");
        return Task.FromResult(true);
    }

    public Task<bool> DisconnectAsync(int sessionId, CancellationToken cancellationToken)
    {
        var i = Sessions.FindIndex(s => s.SessionId == sessionId);
        Sessions[i] = Sessions[i] with { State = SessionState.Disconnected };
        return Task.FromResult(true);
    }

    public Task<bool> LogOffAsync(int sessionId, CancellationToken cancellationToken)
    {
        Sessions.RemoveAll(s => s.SessionId == sessionId);
        return Task.FromResult(true);
    }
}
