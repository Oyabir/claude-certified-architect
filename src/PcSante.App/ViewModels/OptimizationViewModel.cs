using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;
using PcSante.Core.Optimization;

namespace PcSante.App.ViewModels;

public sealed partial class CleanupChoice(CommandId command, string label, string explanation, long bytes) : ObservableObject
{
    public CommandId Command { get; } = command;

    public string Label { get; } = label;

    public string Explanation { get; } = explanation;

    public long Bytes { get; } = bytes;

    public string Size => Loc.Bytes(Bytes);

    [ObservableProperty]
    private bool _selected = bytes > 0;
}

public sealed record StartupRow(string Id, string Name, string Location, bool Enabled)
{
    public string State => Enabled ? Loc.T("Startup_Enabled") : Loc.T("Startup_Disabled");

    public string ActionLabel => Enabled ? Loc.T("Startup_Disable") : Loc.T("Startup_Enable");
}

public sealed record TaskRow(string Path, string Name, string? Author, bool Enabled)
{
    public string ActionLabel => Enabled ? Loc.T("Common_Disable") : Loc.T("Common_Enable");
}

public sealed record PowerRow(Guid Id, string Name, bool IsActive);

public sealed record UndoRow(Guid Id, string Label, string Date);

/// <summary>Optimisation (« Nettoyage » en mode Simple) : nettoyage, démarrage, tâches tierces, alimentation, annulation.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class OptimizationViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Optimization;

    public ObservableCollection<CleanupChoice> Cleanup { get; } = [];

    public ObservableCollection<StartupRow> Startup { get; } = [];

    public ObservableCollection<TaskRow> Tasks { get; } = [];

    public ObservableCollection<PowerRow> PowerPlans { get; } = [];

    public IReadOnlyList<Choice> ServiceProfiles { get; } =
        CommandDefinitions.ServiceProfileNames.Select(p => new Choice(p, Loc.T($"Profile_{p}"))).ToList();

    [ObservableProperty]
    private string _selectedProfile = CommandDefinitions.ServiceProfileNames[0];

    [ObservableProperty]
    private string _profilePreview = string.Empty;

    partial void OnSelectedProfileChanged(string value) => _ = LoadProfilePreviewAsync();

    private async Task LoadProfilePreviewAsync()
    {
        var changes = await Query<List<ServiceChange>>(CommandId.GetServiceProfileChanges, new Dictionary<string, string> { ["profile"] = SelectedProfile }).ConfigureAwait(true);
        ProfilePreview = changes is null ? string.Empty
            : changes.Count == 0 ? Loc.T("Profile_NothingToDo")
            : Loc.F("Profile_Preview", changes.Count, string.Join(", ", changes.Select(c => c.DisplayName)));
    }

    [RelayCommand]
    private Task ApplyProfileAsync() =>
        ExecuteAsync(CommandId.ApplyServiceProfile, new Dictionary<string, string> { ["profile"] = SelectedProfile }, busyKey: "Profile_Applying");

    public ObservableCollection<UndoRow> Undoable { get; } = [];

    [ObservableProperty]
    private string _totalCleanable = string.Empty;

    public bool IsAdvanced => !Main.IsSimpleMode;

    public override async Task LoadAsync()
    {
        var estimate = await Query<CleanupEstimate>(CommandId.GetCleanupEstimate).ConfigureAwait(true);
        Cleanup.Clear();
        if (estimate is not null)
        {
            Cleanup.Add(new CleanupChoice(CommandId.CleanTemporaryFiles, Loc.T("Cleanup_Temp"), Loc.T("Cleanup_TempWhy"), estimate.TemporaryFilesBytes));
            Cleanup.Add(new CleanupChoice(CommandId.EmptyRecycleBin, Loc.T("Cleanup_RecycleBin"), Loc.T("Cleanup_RecycleBinWhy"), estimate.RecycleBinBytes));
            Cleanup.Add(new CleanupChoice(CommandId.CleanBrowserCaches, Loc.T("Cleanup_Browsers"), Loc.T("Cleanup_BrowsersWhy"), estimate.BrowserCachesBytes));
            Cleanup.Add(new CleanupChoice(CommandId.CleanWindowsUpdateCache, Loc.T("Cleanup_Update"), Loc.T("Cleanup_UpdateWhy"), estimate.WindowsUpdateCacheBytes));
            TotalCleanable = Loc.F("Cleanup_Total", Loc.Bytes(estimate.TotalBytes));
        }

        Startup.Clear();
        foreach (var s in (await Query<List<StartupItem>>(CommandId.GetStartupItems).ConfigureAwait(true) ?? []).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Startup.Add(new StartupRow(s.Id, s.Name, Loc.T($"StartupLocation_{s.Location}"), s.Enabled));
        }

        Tasks.Clear();
        PowerPlans.Clear();
        if (IsAdvanced)
        {
            foreach (var t in await Query<List<ThirdPartyTask>>(CommandId.GetThirdPartyTasks).ConfigureAwait(true) ?? [])
            {
                Tasks.Add(new TaskRow(t.Path, t.Name, t.Author, t.Enabled));
            }

            foreach (var p in await Query<List<PowerPlan>>(CommandId.GetPowerPlans).ConfigureAwait(true) ?? [])
            {
                PowerPlans.Add(new PowerRow(p.Id, p.Name, p.IsActive));
            }

            await LoadProfilePreviewAsync().ConfigureAwait(true);
        }

        Undoable.Clear();
        foreach (var u in await Query<List<UndoView>>(CommandId.GetUndoableActions).ConfigureAwait(true) ?? [])
        {
            Undoable.Add(new UndoRow(u.Id, Loc.T($"Command_{u.Command}") + " — " + u.Description, Loc.Date(u.CreatedAt)));
        }

        OnPropertyChanged(nameof(IsAdvanced));
    }

    /// <summary>Bouton principal : nettoie les éléments cochés après UNE confirmation (suppression de fichiers).</summary>
    [RelayCommand]
    private async Task CleanAsync()
    {
        var selected = Cleanup.Where(c => c.Selected && c.Bytes > 0).ToList();
        if (selected.Count == 0)
        {
            Message.Show(Loc.T("Result_NothingToClean"), MessageKind.Info);
            return;
        }

        var list = string.Join(Environment.NewLine, selected.Select(c => $"• {c.Label} ({c.Size})"));
        if (!await Dialogs.ConfirmAsync(Loc.T("Cleanup_Button"), Loc.F("Cleanup_Confirm", list), Loc.T("Cleanup_Button")).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        long freed = 0;
        CommandResult? failure = null;
        try
        {
            foreach (var choice in selected)
            {
                BusyText = Loc.F("Cleanup_Running", choice.Label);
                var result = await Service.RunAsync(choice.Command, confirmed: true).ConfigureAwait(true);
                if (result.IsSuccess && result.MessageArgs.Count > 0 && long.TryParse(result.MessageArgs[0], out var bytes))
                {
                    freed += bytes;
                }
                else if (!result.IsSuccess)
                {
                    failure = result;
                    if (result.Reason is FailureReason.LicenseRequired or FailureReason.ServiceUnavailable or FailureReason.SafeguardFailed)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (failure is not null)
        {
            Message.Show(failure);
        }
        else
        {
            Message.Show(Loc.F("Cleanup_Done", Loc.Bytes(freed)), MessageKind.Success);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ToggleStartupAsync(StartupRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(row.Enabled ? CommandId.DisableStartupItem : CommandId.EnableStartupItem, new Dictionary<string, string> { ["id"] = row.Id });
    }

    [RelayCommand]
    private Task ToggleTaskAsync(TaskRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(row.Enabled ? CommandId.DisableThirdPartyTask : CommandId.EnableThirdPartyTask, new Dictionary<string, string> { ["id"] = row.Path });
    }

    [RelayCommand]
    private Task SetPowerPlanAsync(PowerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.SetPowerPlan, new Dictionary<string, string> { ["plan"] = row.Id.ToString() });
    }

    [RelayCommand]
    private Task UndoAsync(UndoRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.UndoAction, new Dictionary<string, string> { ["undoId"] = row.Id.ToString() });
    }
}
