using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Reporting;
using PcSante.Core.Ui;
using PcSante.Reporting;

namespace PcSante.App.ViewModels;

public sealed record AuditRowView(string Date, string Who, string Action, string Outcome, bool Ok);

[SupportedOSPlatform("windows")]
public sealed partial class ReportsViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Reports;

    [ObservableProperty]
    private bool _monthly = true;

    [ObservableProperty]
    private PointCollection _scorePoints = [];

    [ObservableProperty]
    private string _scoreRange = string.Empty;

    public ObservableCollection<AuditRowView> Journal { get; } = [];

    public bool IsAdvanced => !Main.IsSimpleMode;

    public override async Task LoadAsync()
    {
        var history = await Query<List<ScorePoint>>(CommandId.GetScoreHistory).ConfigureAwait(true) ?? [];
        var daily = history.GroupBy(p => p.At.ToLocalTime().Date).Select(g => g.Last()).TakeLast(60).ToList();
        var points = new PointCollection();
        for (var i = 0; i < daily.Count; i++)
        {
            points.Add(new Point(daily.Count == 1 ? 0 : i * 600.0 / (daily.Count - 1), 100 - daily[i].Score));
        }

        points.Freeze();
        ScorePoints = points;
        ScoreRange = daily.Count == 0 ? Loc.T("Reports_NoHistory")
            : Loc.F("Reports_Range", daily[0].At.ToLocalTime().ToString("d", Loc.Culture), daily[^1].At.ToLocalTime().ToString("d", Loc.Culture));

        Journal.Clear();
        if (IsAdvanced)
        {
            foreach (var a in (await Query<List<AuditEntry>>(CommandId.GetAuditLog).ConfigureAwait(true) ?? []).Take(200))
            {
                Journal.Add(new AuditRowView(Loc.Date(a.Timestamp), a.Who, ReportPdfBuilder.CommandLabel(a.Command, Loc.T), Loc.T($"Outcome_{a.Outcome}"),
                    a.Outcome is AuditOutcome.Succeeded or AuditOutcome.AlreadyDone or AuditOutcome.Started));
            }
        }

        OnPropertyChanged(nameof(IsAdvanced));
    }

    /// <summary>Bouton principal : générer le rapport PDF (hebdomadaire ou mensuel).</summary>
    [RelayCommand]
    private async Task GenerateAsync()
    {
        var period = Monthly ? ReportPeriod.Month : ReportPeriod.Week;
        IsBusy = true;
        BusyText = Loc.T("Reports_Generating");
        try
        {
            var result = await Service.RunAsync(CommandId.GetReportData, new Dictionary<string, string> { ["period"] = period.ToString() }).ConfigureAwait(true);
            if (!result.IsSuccess || result.GetData<ReportData>() is not { } data)
            {
                Message.Show(result);
                return;
            }

            var file = Dialogs.SaveFile(Loc.F("Reports_FileName", DateTime.Now.ToString("yyyy-MM-dd", Loc.Culture)), "Reports_PdfFilter", ".pdf");
            if (file is null)
            {
                return;
            }

            var pdf = await Task.Run(() => ReportPdfBuilder.Build(data, Loc.T, Loc.Culture)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(file, pdf).ConfigureAwait(true);
            Message.Show(Loc.F("Reports_Saved", Path.GetFileName(file)), MessageKind.Success);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
        }
        catch (IOException ex)
        {
            Message.Show(Loc.T("Reports_SaveFailed"), MessageKind.Error, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
