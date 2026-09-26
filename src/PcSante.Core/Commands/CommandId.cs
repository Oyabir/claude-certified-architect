namespace PcSante.Core.Commands;

/// <summary>
/// Liste FERMÉE des commandes acceptées par le service (cahier des charges, section 5).
/// Toute commande absente de cette énumération est refusée. Les valeurs numériques sont figées.
/// </summary>
public enum CommandId
{
    // --- Diagnostic et lecture (M1, M5) ---
    RunHealthAnalysis = 100,
    GetLastHealthReport = 101,
    GetLiveMetrics = 102,
    GetScoreHistory = 103,
    GetSystemInfo = 104,

    // --- Protection antivirus (M2) ---
    GetDefenderStatus = 200,
    StartQuickScan = 201,
    StartFullScan = 202,
    StartCustomScan = 203,
    UpdateSignatures = 204,
    GetThreatHistory = 205,
    GetQuarantine = 206,
    RestoreQuarantinedItem = 207,
    EnableRealtimeProtection = 209,
    EnableCloudProtection = 210,
    EnableControlledFolderAccess = 211,
    GetAntivirusProducts = 212,

    // --- Actions système (M6) ---
    GetFirewallStatus = 300,
    EnableFirewallProfile = 301,
    DisableFirewallProfile = 302,
    ResetFirewallRules = 303,
    GetUpdateStatus = 310,
    SearchUpdates = 311,
    InstallUpdates = 312,
    RepairWindowsUpdate = 313,
    RunSystemFileCheck = 320,
    RunDismRepair = 321,
    CreateRestorePoint = 322,
    EnableSystemRestore = 323,
    FlushDnsCache = 330,
    ResetNetworkStack = 331,
    GetLocalAccounts = 340,
    DisableGuestAccount = 341,
    GetBitLockerStatus = 350,
    GetBitLockerRecoveryKey = 351,
    EnableBitLocker = 352,

    // --- Processus (M3) ---
    GetProcesses = 400,
    GetProcessHistory = 401,
    StopProcess = 402,
    DisableServiceForProcess = 403,

    // --- Optimisations (M4) ---
    GetStartupItems = 500,
    DisableStartupItem = 501,
    EnableStartupItem = 502,
    GetThirdPartyTasks = 503,
    DisableThirdPartyTask = 504,
    EnableThirdPartyTask = 505,
    GetCleanupEstimate = 510,
    CleanTemporaryFiles = 511,
    CleanWindowsUpdateCache = 512,
    EmptyRecycleBin = 513,
    CleanBrowserCaches = 514,
    GetPowerPlans = 520,
    SetPowerPlan = 521,
    GetUndoableActions = 530,
    UndoAction = 531,

    // --- Planification (M9) ---
    GetScheduledTemplates = 600,
    EnableScheduledTemplate = 601,
    DisableScheduledTemplate = 602,
    RunScheduledTemplate = 603,
    GetTaskRunLog = 604,

    // --- Rapports (M8) ---
    GetReportData = 700,
    GetAuditLog = 701,
    GenerateMonthlyReport = 702,

    // --- Licence (section 12) ---
    GetLicenseStatus = 800,
    ActivateLicense = 801,
    TransferLicense = 802,
    DeactivateLicense = 803,
    RevalidateLicense = 804,

    // --- Mises à jour de l'application (section 5) ---
    CheckAppUpdate = 900,
    InstallAppUpdate = 901,
    // --- Optimisations V2 (M4) ---
    GetServiceProfileChanges = 1100,
    ApplyServiceProfile = 1101,
    GetVisualEffects = 1102,
    LightenVisualEffects = 1103,
    GetDiskOptimizationInfo = 1104,
    OptimizeSystemDrive = 1105,
    SetPageFileAutomatic = 1106,

    // --- Sessions (M7) ---
    GetSessions = 1000,
    SendSessionMessage = 1001,
    DisconnectSession = 1002,
    LogOffSession = 1003,
}
