using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

/// <summary>Action système en un clic (M6) avec son explication simple.</summary>
public sealed record SystemActionItem(CommandId Command, string Label, string Explanation, string Duration)
{
    /// <summary>Tuile d'outil : catégorie (petites capitales), icône, titre court, durée courte, verbe du bouton.</summary>
    public string Category => Loc.T($"Tool_Cat_{CategoryKey}").ToUpper(Loc.Culture);

    public string Icon => Command switch
    {
        CommandId.RepairWindowsUpdate => "PcsIcon.refresh",
        CommandId.RunSystemFileCheck => "PcsIcon.wrench",
        CommandId.RunDismRepair => "PcsIcon.monitor",
        CommandId.CreateRestorePoint => "PcsIcon.restore",
        CommandId.EnableSystemRestore => "PcsIcon.check-circle",
        CommandId.FlushDnsCache => "PcsIcon.globe",
        CommandId.ResetNetworkStack => "PcsIcon.wifi",
        CommandId.ResetFirewallRules => "PcsIcon.shield",
        CommandId.InstallUpdates => "PcsIcon.download",
        _ => "PcsIcon.wrench",
    };

    public string Title => Loc.T($"Tool_Title_{Command}");

    public string ShortDescription => Loc.T($"Tool_Desc_{Command}");

    /// <summary>Infobulle : explication complète et durée détaillée.</summary>
    public string FullText => Explanation + Environment.NewLine + Duration;

    public string ShortDuration => Loc.T($"Tool_Duration_{Command}");

    public string Verb => Loc.T($"Tool_Verb_{Command}");

    private string CategoryKey => Command switch
    {
        CommandId.RepairWindowsUpdate or CommandId.InstallUpdates => "Updates",
        CommandId.RunSystemFileCheck or CommandId.RunDismRepair => "Repair",
        CommandId.CreateRestorePoint or CommandId.EnableSystemRestore => "Restore",
        CommandId.ResetFirewallRules => "Firewall",
        _ => "Internet",
    };
}

[SupportedOSPlatform("windows")]
public sealed partial class SystemViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.System;

    [ObservableProperty]
    private string _updateSummary = string.Empty;

    [ObservableProperty]
    private string _systemSummary = string.Empty;

    /// <summary>Outils de réparation (grille de tuiles, dans l'ordre de la maquette 04).</summary>
    public ObservableCollection<SystemActionItem> Tools { get; } = [];

    /// <summary>Bandeau Windows Update : titre, couleur, édition, dates.</summary>
    [ObservableProperty]
    private string _updateTitle = string.Empty;

    [ObservableProperty]
    private Tone _updateTone = Tone.Neutral;

    [ObservableProperty]
    private string _windowsEdition = string.Empty;

    public string BitLockerChip => BitLocker is { } b ? Loc.T($"BitLocker_Chip_{b.State}") : string.Empty;

    public Tone BitLockerTone => BitLocker?.State switch
    {
        BitLockerState.On => Tone.Good,
        BitLockerState.Encrypting or BitLockerState.Decrypting => Tone.Brand,
        BitLockerState.Off or BitLockerState.Paused => Tone.Warn,
        _ => Tone.Neutral,
    };

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public ObservableCollection<SystemActionItem> AccountActions { get; } = [];

    [ObservableProperty]
    private string _accountsSummary = string.Empty;

    [ObservableProperty]
    private string _adminCount = string.Empty;

    /// <summary>Plus de deux administrateurs : le conseil (texte existant) est affiché sous la liste.</summary>
    [ObservableProperty]
    private bool _manyAdmins;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBitLocker), nameof(CanEnableBitLocker), nameof(BitLockerSummary))]
    private BitLockerStatus? _bitLocker;

    /// <summary>Masqué sur Windows Famille et si le disque système n'est pas chiffrable.</summary>
    public bool ShowBitLocker => BitLocker is { Supported: true };

    public bool CanEnableBitLocker => BitLocker is { Supported: true, TpmReady: true, State: BitLockerState.Off };

    public string BitLockerSummary => BitLocker switch
    {
        null or { Supported: false } => string.Empty,
        { State: BitLockerState.Off, TpmReady: false } => Loc.T("BitLocker_TpmNotReady"),
        { State: BitLockerState.Encrypting or BitLockerState.Decrypting } b => Loc.F($"BitLocker_{b.State}", b.Percentage),
        { } b => Loc.T($"BitLocker_{b.State}"),
    };

    public override async Task LoadAsync()
    {
        var info = await Query<SystemInfo>(CommandId.GetSystemInfo).ConfigureAwait(true);
        SystemSummary = info is null ? string.Empty : Loc.F("System_Summary", info.ProductName, info.DisplayVersion, info.MachineName);
        WindowsEdition = info is null ? string.Empty : $"{info.ProductName} {info.DisplayVersion}";
        var status = await Query<UpdateStatus>(CommandId.GetUpdateStatus).ConfigureAwait(true);
        UpdateSummary = status is null ? string.Empty
            : Loc.F(status.RebootRequired ? "System_UpdateSummaryReboot" : "System_UpdateSummary", Loc.When(status.LastSearchAt), Loc.When(status.LastInstallAt));

        // Le nombre de mises à jour en attente n'est pas fourni par le service : le titre ne l'affiche pas.
        (UpdateTitle, UpdateTone) = status switch
        {
            null => (Loc.T("System_UpdateUnknown"), Tone.Neutral),
            { ServiceRunning: false } => (Loc.T("System_UpdateServiceStopped"), Tone.Critical),
            { RebootRequired: true } => (Loc.T("System_UpdateReboot"), Tone.Warn),
            { LastSearchAt: { } last } when DateTimeOffset.Now - last < TimeSpan.FromDays(7) => (Loc.T("System_UpdateChecked"), Tone.Good),
            _ => (Loc.T("System_UpdateToCheck"), Tone.Warn),
        };

        Fill(Tools,
        [
            CommandId.RepairWindowsUpdate, CommandId.RunSystemFileCheck, CommandId.RunDismRepair,
            CommandId.CreateRestorePoint, CommandId.EnableSystemRestore, CommandId.FlushDnsCache,
            CommandId.ResetNetworkStack, CommandId.ResetFirewallRules, CommandId.InstallUpdates,
        ]);

        BitLocker = await Query<BitLockerStatus>(CommandId.GetBitLockerStatus).ConfigureAwait(true);

        var accounts = await Query<List<LocalAccount>>(CommandId.GetLocalAccounts).ConfigureAwait(true) ?? [];
        Accounts.Clear();
        foreach (var a in accounts.Where(a => a.Enabled || a.IsGuest))
        {
            Accounts.Add(new AccountRow(a.Name, Loc.T(a.IsAdministrator ? "Account_Admin" : a.IsGuest ? "Account_Guest" : "Account_Standard"),
                Loc.T(a.Enabled ? "Account_Enabled" : "Account_Disabled"), a.IsAdministrator, a.Enabled));
        }

        var admins = accounts.Count(a => a.IsAdministrator && a.Enabled);
        AccountsSummary = Loc.F(admins > 2 ? "System_AccountsManyAdmins" : "System_AccountsAdmins", admins);
        AdminCount = Loc.F(admins == 1 ? "System_AdminCount_One" : "System_AdminCount_Many", admins);
        ManyAdmins = admins > 2;
        OnPropertyChanged(nameof(BitLockerChip));
        OnPropertyChanged(nameof(BitLockerTone));
        Fill(AccountActions, accounts.Any(a => a.IsGuest && a.Enabled) ? [CommandId.DisableGuestAccount] : []);
    }

    /// <summary>Bouton principal de l'écran.</summary>
    [RelayCommand]
    private Task SearchUpdatesAsync() => ExecuteAsync(CommandId.SearchUpdates, busyKey: "System_Searching");

    [RelayCommand]
    private Task RunAsync(SystemActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ExecuteAsync(item.Command, busyKey: $"Busy_{item.Command}");
    }

    /// <summary>Enregistre la clé de récupération BitLocker dans un fichier choisi par l'utilisateur (idéalement une clé USB).</summary>
    [RelayCommand]
    private Task SaveRecoveryKeyAsync() => SaveRecoveryKeyCoreAsync();

    /// <summary>La clé est toujours enregistrée d'abord : sans elle, le chiffrement n'est pas lancé.</summary>
    [RelayCommand]
    private async Task EnableBitLockerAsync()
    {
        if (Main.NeedsPremium(CommandId.EnableBitLocker))
        {
            Main.ShowPremiumRequired();
            return;
        }

        if (await SaveRecoveryKeyCoreAsync().ConfigureAwait(true))
        {
            await ExecuteAsync(CommandId.EnableBitLocker, new Dictionary<string, string> { ["keySaved"] = "true" }, busyKey: "BitLocker_Starting").ConfigureAwait(true);
        }
    }

    private async Task<bool> SaveRecoveryKeyCoreAsync()
    {
        if (Main.NeedsPremium(CommandId.GetBitLockerRecoveryKey))
        {
            Main.ShowPremiumRequired();
            return false;
        }

        var result = await Service.RunAsync(CommandId.GetBitLockerRecoveryKey).ConfigureAwait(true);
        if (result.GetData<BitLockerRecoveryKey>() is not { } key)
        {
            Message.Show(result);
            return false;
        }

        await Dialogs.ConfirmAsync(Loc.T("BitLocker_SaveKeyTitle"), Loc.T("BitLocker_SaveKeyExplanation"), Loc.T("Common_Continue")).ConfigureAwait(true);
        var shortId = key.KeyId.Trim('{', '}').Split('-')[0];
        var path = Dialogs.SaveFile($"Cle-recuperation-BitLocker-{shortId}.txt", "BitLocker_KeyFileFilter", ".txt");
        if (path is null)
        {
            Message.Show(Loc.T("BitLocker_KeyNotSaved"), MessageKind.Warning);
            return false;
        }

        var machine = Environment.MachineName;
        await File.WriteAllTextAsync(path, Loc.F("BitLocker_KeyFileContent", machine, key.KeyId, key.Password)).ConfigureAwait(true);
        Message.Show(Loc.F("BitLocker_KeySaved", path), MessageKind.Success);
        return true;
    }

    private static void Fill(ObservableCollection<SystemActionItem> target, CommandId[] commands)
    {
        target.Clear();
        foreach (var c in commands)
        {
            target.Add(new SystemActionItem(c, Loc.T($"Command_{c}"), Loc.T($"Explain_{c}"), Loc.T($"Duration_{c}")));
        }
    }
}

/// <summary>Compte local affiché : nom, rôle (administrateur, standard, Invité), état.</summary>
public sealed record AccountRow(string Name, string Role, string State, bool IsAdministrator = false, bool Enabled = true)
{
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpper(Loc.Culture);
}
