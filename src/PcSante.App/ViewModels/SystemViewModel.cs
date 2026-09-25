using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

/// <summary>Action système en un clic (M6) avec son explication simple.</summary>
public sealed record SystemActionItem(CommandId Command, string Label, string Explanation, string Duration);

[SupportedOSPlatform("windows")]
public sealed partial class SystemViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.System;

    [ObservableProperty]
    private string _updateSummary = string.Empty;

    [ObservableProperty]
    private string _systemSummary = string.Empty;

    public ObservableCollection<SystemActionItem> UpdateActions { get; } = [];

    public ObservableCollection<SystemActionItem> RepairActions { get; } = [];

    public ObservableCollection<SystemActionItem> RestoreActions { get; } = [];

    public ObservableCollection<SystemActionItem> FirewallActions { get; } = [];

    public ObservableCollection<SystemActionItem> NetworkActions { get; } = [];

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public ObservableCollection<SystemActionItem> AccountActions { get; } = [];

    [ObservableProperty]
    private string _accountsSummary = string.Empty;

    public override async Task LoadAsync()
    {
        var info = await Query<SystemInfo>(CommandId.GetSystemInfo).ConfigureAwait(true);
        SystemSummary = info is null ? string.Empty : Loc.F("System_Summary", info.ProductName, info.DisplayVersion, info.MachineName);
        var status = await Query<UpdateStatus>(CommandId.GetUpdateStatus).ConfigureAwait(true);
        UpdateSummary = status is null ? string.Empty
            : Loc.F(status.RebootRequired ? "System_UpdateSummaryReboot" : "System_UpdateSummary", Loc.Date(status.LastSearchAt), Loc.Date(status.LastInstallAt));

        Fill(UpdateActions, [CommandId.InstallUpdates, CommandId.RepairWindowsUpdate]);
        Fill(RepairActions, [CommandId.RunSystemFileCheck, CommandId.RunDismRepair]);
        Fill(RestoreActions, [CommandId.CreateRestorePoint, CommandId.EnableSystemRestore]);
        Fill(FirewallActions, [CommandId.ResetFirewallRules]);
        Fill(NetworkActions, [CommandId.FlushDnsCache, CommandId.ResetNetworkStack]);

        var accounts = await Query<List<LocalAccount>>(CommandId.GetLocalAccounts).ConfigureAwait(true) ?? [];
        Accounts.Clear();
        foreach (var a in accounts.Where(a => a.Enabled || a.IsGuest))
        {
            Accounts.Add(new AccountRow(a.Name, Loc.T(a.IsAdministrator ? "Account_Admin" : a.IsGuest ? "Account_Guest" : "Account_Standard"),
                Loc.T(a.Enabled ? "Account_Enabled" : "Account_Disabled")));
        }

        var admins = accounts.Count(a => a.IsAdministrator && a.Enabled);
        AccountsSummary = Loc.F(admins > 2 ? "System_AccountsManyAdmins" : "System_AccountsAdmins", admins);
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
public sealed record AccountRow(string Name, string Role, string State);
