using System.Collections.Frozen;

namespace PcSante.Core.Commands;

/// <summary>
/// Définition du catalogue fermé : chaque commande, son offre, sa sauvegarde, sa confirmation et ses paramètres.
/// Le service refuse toute commande qui n'est pas décrite ici.
/// </summary>
public static class CommandDefinitions
{
    public static readonly IReadOnlyList<string> FirewallProfiles = ["Domain", "Private", "Public"];
    public static readonly IReadOnlyList<string> ScanTypes = ["Quick", "Full"];
    public static readonly IReadOnlyList<string> TemplateIds = Enum.GetNames<Scheduling.ScheduledTemplateId>();
    public static readonly IReadOnlyList<string> Days =
        ["Everyday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", Scheduling.ScheduleSettings.MonthStart];
    public static readonly IReadOnlyList<string> Languages = ["fr", "en", "ar"];
    public static readonly IReadOnlyList<string> SessionMessages = ["SaveWork", "RestartSoon", "Maintenance"];
    public static readonly IReadOnlyList<string> ServiceProfileNames = Enum.GetNames<Optimization.ServiceProfile>();
    public static readonly IReadOnlyList<string> Periods = ["Week", "Month"];

    private static readonly ParameterSpec[] None = [];

    private static CommandDescriptor Q(CommandId id, RequiredTier tier = RequiredTier.Free, params ParameterSpec[] p) =>
        new(id, CommandKind.Query, tier, SafeguardKind.None, ConfirmationKind.None, p);

    private static CommandDescriptor A(
        CommandId id,
        SafeguardKind safeguard,
        ConfirmationKind confirmation = ConfirmationKind.None,
        RequiredTier tier = RequiredTier.Premium,
        params ParameterSpec[] p) =>
        new(id, CommandKind.Action, tier, safeguard, confirmation, p);

    private static readonly ParameterSpec Profile = new("profile", ParameterType.Choice, AllowedValues: FirewallProfiles);
    private static readonly ParameterSpec ItemId = new("id", ParameterType.Identifier);
    private static readonly ParameterSpec Key = new("key", ParameterType.LicenseKey);
    private static readonly ParameterSpec Template = new("template", ParameterType.Choice, AllowedValues: TemplateIds);
    private static readonly ParameterSpec SessionId = new("sessionId", ParameterType.PositiveInteger);
    private static readonly ParameterSpec ServiceProfileParameter = new("profile", ParameterType.Choice, AllowedValues: ServiceProfileNames);

    public static FrozenDictionary<CommandId, CommandDescriptor> All { get; } = new[]
    {
        // Diagnostic : gratuit (section 6 : score, diagnostic, mini-affichage)
        Q(CommandId.RunHealthAnalysis),
        Q(CommandId.GetLastHealthReport),
        Q(CommandId.GetLiveMetrics),
        Q(CommandId.GetScoreHistory),
        Q(CommandId.GetSystemInfo),

        // Defender
        Q(CommandId.GetDefenderStatus),
        A(CommandId.StartQuickScan, SafeguardKind.None),
        A(CommandId.StartFullScan, SafeguardKind.None),
        A(CommandId.StartCustomScan, SafeguardKind.None, p: new ParameterSpec("path", ParameterType.LocalPath)),
        A(CommandId.UpdateSignatures, SafeguardKind.None),
        Q(CommandId.GetThreatHistory),
        Q(CommandId.GetQuarantine),
        A(CommandId.RestoreQuarantinedItem, SafeguardKind.RestorePoint, ConfirmationKind.ReducesProtection, p: ItemId),
        A(CommandId.EnableRealtimeProtection, SafeguardKind.OwnBackup),
        A(CommandId.EnableCloudProtection, SafeguardKind.OwnBackup),
        A(CommandId.EnableControlledFolderAccess, SafeguardKind.OwnBackup),

        // Pare-feu
        Q(CommandId.GetFirewallStatus),
        Q(CommandId.GetAntivirusProducts),
        A(CommandId.EnableFirewallProfile, SafeguardKind.OwnBackup, p: Profile),
        A(CommandId.DisableFirewallProfile, SafeguardKind.OwnBackup, ConfirmationKind.ReducesProtection, p: Profile),
        A(CommandId.ResetFirewallRules, SafeguardKind.RestorePointAndOwnBackup, ConfirmationKind.ReducesProtection),
        A(CommandId.FlushDnsCache, SafeguardKind.None),
        A(CommandId.ResetNetworkStack, SafeguardKind.RestorePoint, ConfirmationKind.RestartsComputer),
        Q(CommandId.GetLocalAccounts),
        A(CommandId.DisableGuestAccount, SafeguardKind.OwnBackup),
        Q(CommandId.GetBitLockerStatus),

        // Console PME : rattachement du poste (métriques techniques seulement)
        Q(CommandId.GetPmeStatus),
        A(CommandId.EnrollInPme, SafeguardKind.None, p: new ParameterSpec("code", ParameterType.EnrollmentCode)),
        A(CommandId.LeavePme, SafeguardKind.None),

        // Profils de services (M4) : passage en Manuel seulement, point de restauration + sauvegarde pour « Annuler »
        Q(CommandId.GetServiceProfileChanges, RequiredTier.Free, ServiceProfileParameter),
        A(CommandId.ApplyServiceProfile, SafeguardKind.RestorePointAndOwnBackup, p: ServiceProfileParameter),
        Q(CommandId.GetVisualEffects),
        A(CommandId.LightenVisualEffects, SafeguardKind.RestorePointAndOwnBackup),
        Q(CommandId.GetDiskOptimizationInfo),
        A(CommandId.OptimizeSystemDrive, SafeguardKind.RestorePoint),
        A(CommandId.SetPageFileAutomatic, SafeguardKind.RestorePointAndOwnBackup, ConfirmationKind.RestartsComputer),

        // Sessions (M7) : messages d'une liste fermée, actions réservées aux administrateurs (vérifié par le service)
        Q(CommandId.GetSessions),
        A(CommandId.SendSessionMessage, SafeguardKind.None, p:
        [
            SessionId,
            new ParameterSpec("message", ParameterType.Choice, AllowedValues: SessionMessages),
            new ParameterSpec("language", ParameterType.Choice, Required: false, AllowedValues: Languages),
        ]),
        A(CommandId.DisconnectSession, SafeguardKind.None, ConfirmationKind.InterruptsUser, p: SessionId),
        A(CommandId.LogOffSession, SafeguardKind.None, ConfirmationKind.ClosesProgram, p: SessionId),
        A(CommandId.GetBitLockerRecoveryKey, SafeguardKind.None),
        A(CommandId.EnableBitLocker, SafeguardKind.None, ConfirmationKind.EncryptsDisk, p: new ParameterSpec("keySaved", ParameterType.Boolean)),

        // Windows Update
        Q(CommandId.GetUpdateStatus),
        A(CommandId.SearchUpdates, SafeguardKind.None),
        A(CommandId.InstallUpdates, SafeguardKind.RestorePoint, ConfirmationKind.RestartsComputer),
        A(CommandId.RepairWindowsUpdate, SafeguardKind.RestorePointAndOwnBackup),

        // Système
        A(CommandId.RunSystemFileCheck, SafeguardKind.RestorePoint),
        A(CommandId.RunDismRepair, SafeguardKind.RestorePoint),
        A(CommandId.CreateRestorePoint, SafeguardKind.None),
        A(CommandId.EnableSystemRestore, SafeguardKind.None),

        // Processus
        Q(CommandId.GetProcesses),
        Q(CommandId.GetProcessHistory),
        A(CommandId.StopProcess, SafeguardKind.None, ConfirmationKind.ClosesProgram,
            p: new ParameterSpec("pid", ParameterType.PositiveInteger)),
        A(CommandId.DisableServiceForProcess, SafeguardKind.RestorePointAndOwnBackup, ConfirmationKind.ClosesProgram,
            p: new ParameterSpec("service", ParameterType.Identifier)),

        // Optimisations
        Q(CommandId.GetStartupItems),
        A(CommandId.DisableStartupItem, SafeguardKind.RestorePointAndOwnBackup, p: ItemId),
        A(CommandId.EnableStartupItem, SafeguardKind.OwnBackup, p: ItemId),
        Q(CommandId.GetThirdPartyTasks),
        A(CommandId.DisableThirdPartyTask, SafeguardKind.RestorePointAndOwnBackup, p: ItemId),
        A(CommandId.EnableThirdPartyTask, SafeguardKind.OwnBackup, p: ItemId),
        Q(CommandId.GetCleanupEstimate),
        A(CommandId.CleanTemporaryFiles, SafeguardKind.RestorePoint, ConfirmationKind.DeletesFiles),
        A(CommandId.CleanWindowsUpdateCache, SafeguardKind.RestorePoint, ConfirmationKind.DeletesFiles),
        A(CommandId.EmptyRecycleBin, SafeguardKind.RestorePoint, ConfirmationKind.DeletesFiles),
        A(CommandId.CleanBrowserCaches, SafeguardKind.RestorePoint, ConfirmationKind.DeletesFiles),
        Q(CommandId.GetPowerPlans),
        A(CommandId.SetPowerPlan, SafeguardKind.RestorePointAndOwnBackup,
            p: new ParameterSpec("plan", ParameterType.Guid)),
        Q(CommandId.GetUndoableActions),
        A(CommandId.UndoAction, SafeguardKind.None, p: new ParameterSpec("undoId", ParameterType.Guid)),

        // Planification
        Q(CommandId.GetScheduledTemplates),
        A(CommandId.EnableScheduledTemplate, SafeguardKind.None, p:
        [
            Template,
            new ParameterSpec("day", ParameterType.Choice, AllowedValues: Days),
            new ParameterSpec("time", ParameterType.TimeOfDay),
            new ParameterSpec("onlyWhenIdle", ParameterType.Boolean),
            new ParameterSpec("onlyOnAcPower", ParameterType.Boolean),
            new ParameterSpec("language", ParameterType.Choice, Required: false, AllowedValues: Languages),
        ]),
        A(CommandId.DisableScheduledTemplate, SafeguardKind.None, p: Template),
        A(CommandId.RunScheduledTemplate, SafeguardKind.None, p:
        [
            Template,
            new ParameterSpec("language", ParameterType.Choice, Required: false, AllowedValues: Languages),
        ]),
        Q(CommandId.GetTaskRunLog),

        // Rapports
        Q(CommandId.GetReportData, RequiredTier.Premium, new ParameterSpec("period", ParameterType.Choice, AllowedValues: Periods)),
        Q(CommandId.GetAuditLog),
        A(CommandId.GenerateMonthlyReport, SafeguardKind.None, p: new ParameterSpec("language", ParameterType.Choice, Required: false, AllowedValues: Languages)),

        // Licence : toujours accessible, sinon impossible d'activer
        Q(CommandId.GetLicenseStatus),
        A(CommandId.ActivateLicense, SafeguardKind.None, tier: RequiredTier.Free, p: Key),
        A(CommandId.TransferLicense, SafeguardKind.None, tier: RequiredTier.Free, p: Key),
        A(CommandId.DeactivateLicense, SafeguardKind.None, ConfirmationKind.ReducesProtection, RequiredTier.Free),
        A(CommandId.RevalidateLicense, SafeguardKind.None, tier: RequiredTier.Free),

        // Mises à jour (MSI transactionnel : l'installeur gère lui-même le retour arrière)
        Q(CommandId.CheckAppUpdate),
        A(CommandId.InstallAppUpdate, SafeguardKind.None, ConfirmationKind.ClosesProgram, RequiredTier.Free),
    }.ToFrozenDictionary(d => d.Id);

    public static bool TryGet(CommandId id, out CommandDescriptor descriptor) =>
        All.TryGetValue(id, out descriptor!);

    public static CommandDescriptor Get(CommandId id) =>
        All.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Commande hors catalogue : {id}");
}
