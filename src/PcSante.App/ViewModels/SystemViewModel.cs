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
