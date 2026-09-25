using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Processes;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

public sealed record ProcessRow(ProcessView Process, string? StartupId)
{
    public string Name => Process.Name;

    /// <summary>À quoi sert ce programme (base de réputation), vide s'il est inconnu.</summary>
    public string Description => Process.DescriptionKey is { } key ? Loc.T($"Proc_{key}") : string.Empty;

    public double Cpu => Math.Round(Process.CpuPercent, 1);

    public long MemoryBytes => Process.MemoryBytes;

    public string Memory => Loc.Bytes(Process.MemoryBytes);

    public string Disk => Loc.Bytes((long)Process.DiskBytesPerSecond) + Loc.T("Unit_PerSecond");

    public string Network => Loc.T("Common_NotAvailable");

    public Reputation Reputation => Process.Reputation;

    public string ReputationLabel => Loc.T($"Reputation_{Process.Reputation}");

    public string Signed => Process.IsSigned ? (Process.Publisher ?? Loc.T("Process_Signed")) : Loc.T("Process_NotSigned");

    public bool CanStop => !Process.IsProtected;

    public bool HasService => Process.ServiceNames.Count > 0 && !Process.IsProtected;

    public bool CanRemoveFromStartup => StartupId is not null;

    public bool HasLocation => Process.ExecutablePath is not null;
}

public sealed record HistoryRow(string Name, string Cpu, string Memory, string Presence);

[SupportedOSPlatform("windows")]
public sealed partial class ProcessesViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Processes;

    public ObservableCollection<ProcessRow> Processes { get; } = [];

    public ObservableCollection<HistoryRow> History { get; } = [];

    public override async Task LoadAsync()
    {
        var list = await Query<List<ProcessView>>(CommandId.GetProcesses).ConfigureAwait(true) ?? [];
        var startup = await Query<List<StartupItem>>(CommandId.GetStartupItems).ConfigureAwait(true) ?? [];
        Processes.Clear();
        foreach (var p in list)
        {
            var match = startup.FirstOrDefault(s => s.Enabled && s.ExecutablePath is not null && p.ExecutablePath is not null
                && string.Equals(Path.GetFullPath(s.ExecutablePath), p.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            Processes.Add(new ProcessRow(p, match?.Id));
        }

        History.Clear();
        foreach (var h in await Query<List<ProcessHistoryEntry>>(CommandId.GetProcessHistory).ConfigureAwait(true) ?? [])
        {
            History.Add(new HistoryRow(h.Name, Loc.F("Metric_Percent", h.AverageCpuPercent), Loc.Bytes(h.AverageMemoryBytes),
                Loc.F("Process_HighUsage", Math.Round(h.HighUsageRatio * 100))));
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private Task StopAsync(ProcessRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.StopProcess, new Dictionary<string, string> { ["pid"] = row.Process.ProcessId.ToString(Loc.Culture) }, row.Name);
    }

    [RelayCommand]
    private Task DisableServiceAsync(ProcessRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.DisableServiceForProcess, new Dictionary<string, string> { ["service"] = row.Process.ServiceNames[0] }, row.Process.ServiceNames[0]);
    }

    [RelayCommand]
    private Task RemoveFromStartupAsync(ProcessRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.DisableStartupItem, new Dictionary<string, string> { ["id"] = row.StartupId! });
    }

    /// <summary>Ouvre l'explorateur sur le fichier : action de l'utilisateur, sans privilège, pas une action système.</summary>
    [RelayCommand]
    private static void OpenLocation(ProcessRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Process.ExecutablePath is { } path && File.Exists(path))
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
            start.ArgumentList.Add("/select," + path);
            Process.Start(start)?.Dispose();
        }
    }
}
