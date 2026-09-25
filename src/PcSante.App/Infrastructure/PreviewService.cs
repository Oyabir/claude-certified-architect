#if DEBUG
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Licensing;
using PcSante.Core.Optimization;
using PcSante.Core.Processes;
using PcSante.Core.Scheduling;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.Infrastructure;

/// <summary>
/// Outil de développement (compilé en Debug seulement, lancé avec « --apercu ») : réponses fictives,
/// calquées sur les maquettes de docs/redesign, pour vérifier la présentation sans le service.
/// Absent de la version livrée (Release).
/// </summary>
internal static class PreviewService
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    public static CommandResult Answer(CommandId command, IReadOnlyDictionary<string, string>? parameters) => command switch
    {
        CommandId.GetLicenseStatus => CommandResult.WithData(new LicenseStatus
        {
            State = LicenseState.Active,
            EffectiveTier = LicenseTier.Premium,
            KeyHint = "PCS-…-7Q2K",
            LicenseExpiresAt = Now.AddMonths(11),
            TokenExpiresAt = Now.AddDays(14),
            LastValidatedAt = Now.AddHours(-3),
            Seats = 1,
        }),
        CommandId.GetLastHealthReport or CommandId.RunHealthAnalysis => CommandResult.WithData(Report()),
        CommandId.GetDefenderStatus => CommandResult.WithData(new DefenderStatus
        {
            IsAvailable = true,
            IsActiveAntivirus = true,
            RealTimeProtectionEnabled = true,
            CloudProtectionEnabled = true,
            ControlledFolderAccessEnabled = false,
            TamperProtectionEnabled = true,
            SignaturesUpdatedAt = Now.AddHours(-5),
            SignatureVersion = "1.419.212.0",
            LastQuickScanAt = Now.AddDays(-1),
            LastFullScanAt = Now.AddDays(-12),
        }),
        CommandId.GetAntivirusProducts => CommandResult.WithData(new List<AntivirusProduct> { new("Windows Defender", true, true, true) }),
        CommandId.GetFirewallStatus => CommandResult.WithData(new List<FirewallProfileStatus>
        {
            new(FirewallProfile.Domain, true), new(FirewallProfile.Private, true), new(FirewallProfile.Public, true),
        }),
        CommandId.GetThreatHistory => CommandResult.WithData(new List<ThreatInfo>
        {
            new("1", "Trojan:Win32/Wacatac.B!ml", 5, Now.AddDays(-6), "Handled", @"C:\Users\Nadia\Downloads\facture_setup.exe"),
            new("2", "PUA:Win32/Presenoker", 1, Now.AddDays(-21), "Handled", @"C:\Users\Nadia\Downloads\convertisseur.exe"),
        }),
        CommandId.GetQuarantine => CommandResult.WithData(new List<QuarantineItem>()),
        CommandId.GetCleanupEstimate => CommandResult.WithData(new CleanupEstimate(612L << 20, 402L << 20, 118L << 20, 96L << 20)),
        CommandId.GetStartupItems => CommandResult.WithData(new List<StartupItem>
        {
            new("1", "Microsoft Teams", "teams.exe", StartupLocation.UserRun, true, null),
            new("2", "OneDrive", "onedrive.exe", StartupLocation.UserRun, true, null),
            new("3", "Spotify", "spotify.exe", StartupLocation.UserRun, false, null),
            new("4", "Adobe Acrobat Update", "armsvc.exe", StartupLocation.MachineRun, true, null),
            new("5", "Zoom", "zoom.exe", StartupLocation.UserStartupFolder, false, null),
        }),
        CommandId.GetThirdPartyTasks => CommandResult.WithData(new List<ThirdPartyTask>
        {
            new(@"\GoogleUpdateTaskMachineCore", "GoogleUpdateTaskMachineCore", "Google LLC", true),
            new(@"\OneDrive Standalone Update Task-S-1-5-21-3623811015-3361044348-30300820-1013", "OneDrive Standalone Update Task-S-1-5-21-3623811015-3361044348-30300820-1013", "Microsoft Corporation", true),
            new(@"\Adobe Acrobat Update Task", "Adobe Acrobat Update Task", "Adobe Systems", false),
        }),
        CommandId.GetPowerPlans => CommandResult.WithData(new List<PowerPlan>
        {
            new(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Utilisation normale", true),
            new(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "Performances élevées", false),
        }),
        CommandId.GetVisualEffects => CommandResult.WithData(new VisualEffectsSettings(null, "1", 1, 0)),
        CommandId.GetDiskOptimizationInfo => CommandResult.WithData(new DiskOptimizationInfo("C:", DiskMediaType.Ssd, true, null)),
        CommandId.GetServiceProfileChanges => CommandResult.WithData(new List<ServiceChange>
        {
            new("Fax", "Télécopie", ServiceStartMode.Automatic), new("XblGameSave", "Sauvegarde de jeux Xbox Live", ServiceStartMode.Automatic),
        }),
        CommandId.GetUndoableActions => CommandResult.WithData(new List<UndoView>
        {
            new(Guid.NewGuid(), CommandId.DisableStartupItem, Now.AddDays(-1), "Spotify"),
            new(Guid.NewGuid(), CommandId.SetPowerPlan, Now.AddDays(-3), "Utilisation normale"),
        }),
        CommandId.GetLiveMetrics => CommandResult.WithData(Metrics()),
        CommandId.GetProcesses => CommandResult.WithData(new List<ProcessView>
        {
            new(4120, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 12.4, 1_240L << 20, 220_000, [], true, "Google LLC", Reputation.Useful, false, "ProcessDesc_Browser"),
            new(2210, "Teams.exe", null, 4.1, 612L << 20, 12_000, [], true, "Microsoft Corporation", Reputation.Useful, false),
            new(3302, "updater_x.exe", @"C:\Users\Nadia\AppData\Local\Temp\updater_x.exe", 0.4, 38L << 20, 0, [], false, null, Reputation.Unknown, false),
            new(1880, "OneDrive.exe", null, 0.8, 146L << 20, 4_000, [], true, "Microsoft Corporation", Reputation.Useful, false),
        }),
        CommandId.GetProcessHistory => CommandResult.WithData(new List<ProcessHistoryEntry>
        {
            new("chrome.exe", 14.2, 1_100L << 20, 2016, 0.31), new("Teams.exe", 6.8, 590L << 20, 2016, 0.12),
        }),
        CommandId.GetSystemInfo => CommandResult.WithData(new SystemInfo("Windows 11 Pro", "24H2", 26100, false, "PC-BUREAU-NADIA", true)),
        CommandId.GetUpdateStatus => CommandResult.WithData(new UpdateStatus(Now.AddHours(-20), Now.AddDays(-6), false, true)),
        CommandId.GetBitLockerStatus => CommandResult.WithData(new BitLockerStatus(true, true, BitLockerState.Off, 0, false)),
        CommandId.GetLocalAccounts => CommandResult.WithData(new List<LocalAccount>
        {
            new("Nadia", "S-1-5-21-1-1001", true, true), new("Comptabilite", "S-1-5-21-1-1002", true, false), new("Invité", "S-1-5-21-1-501", false, false),
        }),
        CommandId.GetSessions => CommandResult.WithData(new List<UserSession>
        {
            new(1, @"PC-BUREAU-NADIA\Nadia", SessionState.Active, false, null, Now.AddHours(-2)),
            new(2, @"PC-BUREAU-NADIA\Comptabilite", SessionState.Disconnected, true, "192.168.1.24", Now.AddDays(-1)),
        }),
        CommandId.GetScheduledTemplates => CommandResult.WithData(new List<TemplateView>
        {
            new(ScheduledTemplateId.DailyAntivirusScan, true, new ScheduleSettings(null, new TimeOnly(12, 30), true, false), [CommandId.StartQuickScan],
                new TaskRunEntry(ScheduledTemplateId.DailyAntivirusScan, Now.AddDays(-1), Now.AddDays(-1).AddMinutes(4), true, "Result_Ok")),
            new(ScheduledTemplateId.WeeklyCleanup, false, new ScheduleSettings(DayOfWeek.Sunday, new TimeOnly(10, 0), true, true), [CommandId.CleanTemporaryFiles], null),
            new(ScheduledTemplateId.WeeklyRestorePoint, false, new ScheduleSettings(DayOfWeek.Monday, new TimeOnly(9, 0), false, false), [CommandId.CreateRestorePoint], null),
            new(ScheduledTemplateId.WeeklyUpdateCheck, false, new ScheduleSettings(DayOfWeek.Wednesday, new TimeOnly(11, 0), false, false), [CommandId.SearchUpdates], null),
            new(ScheduledTemplateId.MonthlyReport, false, new ScheduleSettings(null, new TimeOnly(9, 0), false, false, true), [CommandId.GenerateMonthlyReport], null),
        }),
        CommandId.GetTaskRunLog => CommandResult.WithData(new List<TaskRunEntry>
        {
            new(ScheduledTemplateId.DailyAntivirusScan, Now.AddDays(-1), Now.AddDays(-1).AddMinutes(4), true, "Result_Ok"),
        }),
        CommandId.GetScoreHistory => CommandResult.WithData(Enumerable.Range(0, 30)
            .Select(i => new ScorePoint(Now.AddDays(i - 29), 70 + (int)(12 * Math.Sin(i / 4.0)) + (i / 3), new SubScores(90, 80, 85, 60))).ToList()),
        CommandId.GetAuditLog => CommandResult.WithData(new List<AuditEntry>
        {
            Audit(-0.1, "Nadia", CommandId.RunHealthAnalysis, AuditOutcome.Succeeded),
            Audit(-1, "Nadia", CommandId.CleanTemporaryFiles, AuditOutcome.Succeeded),
            Audit(-1.2, "Planificateur", CommandId.StartQuickScan, AuditOutcome.Succeeded),
            Audit(-3, "Nadia", CommandId.DisableStartupItem, AuditOutcome.Succeeded),
            Audit(-4, "Nadia", CommandId.RunSystemFileCheck, AuditOutcome.Failed),
        }),
        CommandId.GetPmeStatus => CommandResult.WithData(new ViewModels.PmeStatusView(true, false, null, null, Core.Pme.PmeError.None)),
        CommandId.CheckAppUpdate => CommandResult.WithData(new Core.Updates.AppUpdateInfo(false, "1.0.0", null)),
        _ when CommandDefinitions.Get(command).Kind == CommandKind.Query => CommandResult.WithData<object?>(null),
        _ => CommandResult.Success("Result_Ok"),
    };

    private static HealthReport Report() => new(
        Now.AddMinutes(-42),
        76,
        HealthScoreCalculator.ColorOf(76),
        new SubScores(100, 100, 92, 58),
        [
            new HealthIssue("LowDiskSpace", HealthCategory.Storage, IssueSeverity.Warning, IssueFix.Open(ScreenId.Optimization), ["28.7"]),
            new HealthIssue("CleanableFiles", HealthCategory.Storage, IssueSeverity.Info, IssueFix.Open(ScreenId.Optimization), ["1.2"]),
            new HealthIssue("NoRecentRestorePoint", HealthCategory.Stability, IssueSeverity.Info, IssueFix.Run(CommandId.CreateRestorePoint), []),
        ],
        TimeSpan.FromSeconds(24));

    private static int _tick;

    private static LiveMetrics Metrics()
    {
        var t = _tick++;
        return new LiveMetrics
        {
            At = DateTimeOffset.Now,
            CpuPercent = 18 + (12 * Math.Sin(t / 3.0)) + (t % 5),
            MemoryPercent = 67 + (2 * Math.Sin(t / 7.0)),
            MemoryUsedBytes = (long)(10.6 * (1L << 30)),
            MemoryTotalBytes = (long)(15.8 * (1L << 30)),
            DiskActivityPercent = Math.Abs(8 * Math.Sin(t / 2.0)) + 2,
            NetworkReceivedBytesPerSecond = 180_000 + (long)(120_000 * Math.Sin(t / 2.5)),
            NetworkSentBytesPerSecond = 40_000 + (long)(20_000 * Math.Cos(t / 3.0)),
            CpuTemperatureCelsius = 54,
            BatteryPercent = 82,
            OnAcPower = true,
        };
    }

    private static AuditEntry Audit(double days, string who, CommandId command, AuditOutcome outcome) => new()
    {
        Timestamp = Now.AddDays(days),
        Who = who,
        Command = command.ToString(),
        Outcome = outcome,
    };
}
#endif
