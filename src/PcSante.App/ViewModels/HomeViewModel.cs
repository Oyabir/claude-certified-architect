using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

/// <summary>Problème affiché : ce qui ne va pas, pourquoi c'est important, bouton « Corriger ».</summary>
public sealed record IssueItem(HealthIssue Issue, string Title, string Why, string Category, string FixLabel)
{
    public IssueSeverity Severity => Issue.Severity;

    public bool CanFix => Issue.Fix is not null;
}

public sealed record SubScoreItem(string Label, int Value);

[SupportedOSPlatform("windows")]
public sealed partial class HomeViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Home;

    [ObservableProperty]
    private HealthReport? _report;

    public ObservableCollection<IssueItem> Issues { get; } = [];

    public ObservableCollection<SubScoreItem> SubScores { get; } = [];

    public int Score => Report?.Score ?? 0;

    public HealthColor Color => Report?.Color ?? HealthColor.Orange;

    public string ScoreLabel => Report is null ? Loc.T("Home_NoAnalysis") : Loc.T($"Color_{Report.Color}");

    public string LastAnalysis => Report is null ? string.Empty : Loc.F("Home_LastAnalysis", Loc.Date(Report.AnalyzedAt));

    public bool HasReport => Report is not null;

    public bool HasIssues => Issues.Count > 0;

    /// <summary>
    /// Un seul bouton principal : « Tout corriger » s'il y a des corrections possibles et l'offre Premium,
    /// sinon « Analyser mon PC » (en Gratuit, « Tout corriger · Premium » reste visible en bouton secondaire).
    /// </summary>
    public bool PrimaryIsFixAll => Main.IsPremium && HasFixable;

    public bool ShowFixAllPremium => !Main.IsPremium && HasFixable;

    public string FixAllPremiumLabel => Loc.F("Common_WithPremium", Loc.T("Home_FixAll"));

    private bool HasFixable => Issues.Any(i => i.Issue.Fix?.Command is not null);

    public override async Task LoadAsync()
    {
        var report = await Query<HealthReport>(CommandId.GetLastHealthReport).ConfigureAwait(true);
        if (report is null && Main.ServiceAvailable)
        {
            await AnalyzeAsync().ConfigureAwait(true);
            return;
        }

        SetReport(report);
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("Home_Analyzing");
        try
        {
            var result = await Service.RunAsync(CommandId.RunHealthAnalysis).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                Message.Show(result);
                Main.ServiceAvailable = result.Reason != FailureReason.ServiceUnavailable;
                return;
            }

            SetReport(result.GetData<HealthReport>());
            Message.Show(Loc.F("Home_AnalysisDone", Score), MessageKind.Success);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FixAsync(IssueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var fix = item.Issue.Fix;
        if (fix is null)
        {
            return;
        }

        if (fix.Command is not { } command)
        {
            Main.Navigate(fix.Screen ?? ScreenId.Home);
            return;
        }

        var result = await ExecuteAsync(command, fix.Parameters, item.Title, reload: false).ConfigureAwait(true);
        if (result?.IsSuccess == true)
        {
            await ReanalyzeQuietlyAsync().ConfigureAwait(true);
        }
    }

    /// <summary>« Tout corriger » : récapitulatif avant exécution, puis corrections une à une.</summary>
    [RelayCommand]
    private async Task FixAllAsync()
    {
        var fixable = Issues.Where(i => i.Issue.Fix?.Command is not null).ToList();
        if (fixable.Count == 0)
        {
            return;
        }

        if (!Main.IsPremium)
        {
            Main.ShowPremiumRequired();
            return;
        }

        var recap = string.Join(Environment.NewLine, fixable.Select(i => "• " + i.FixLabel + " — " + i.Title));
        if (!await Dialogs.ConfirmAsync(Loc.T("Home_FixAll"), Loc.F("Home_FixAllRecap", recap), Loc.T("Home_FixAll")).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        int ok = 0, failed = 0;
        CommandResult? lastFailure = null;
        try
        {
            foreach (var item in fixable)
            {
                BusyText = Loc.F("Home_Fixing", item.Title);
                var result = await Service.RunAsync(item.Issue.Fix!.Command!.Value, item.Issue.Fix.Parameters).ConfigureAwait(true);
                if (result.IsSuccess)
                {
                    ok++;
                }
                else
                {
                    failed++;
                    lastFailure = result;
                }

                if (result.Reason is FailureReason.LicenseRequired or FailureReason.ServiceUnavailable)
                {
                    Message.Show(result);
                    return;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await ReanalyzeQuietlyAsync().ConfigureAwait(true);
        if (failed == 0)
        {
            Message.Show(Loc.F("Home_FixAllDone", ok), MessageKind.Success);
        }
        else
        {
            Message.Show(Loc.F("Home_FixAllPartial", ok, failed), MessageKind.Warning,
                lastFailure is null ? null : ResultMessage.Describe(lastFailure) + Environment.NewLine + lastFailure.TechnicalDetails);
        }
    }

    private string FixLabelOf(IssueFix? fix)
    {
        if (fix?.Command is not { } command)
        {
            return Loc.T("Fix_Open");
        }

        var label = Loc.T($"Fix_{command}");
        return Main.NeedsPremium(command) ? Loc.F("Common_WithPremium", label) : label;
    }

    private async Task ReanalyzeQuietlyAsync()
    {
        var result = await Service.RunAsync(CommandId.RunHealthAnalysis).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            SetReport(result.GetData<HealthReport>());
        }
    }

    private void SetReport(HealthReport? report)
    {
        Report = report;
        Issues.Clear();
        SubScores.Clear();
        if (report is not null)
        {
            foreach (var issue in report.Issues)
            {
                var args = issue.Args.Cast<object?>().ToArray();
                Issues.Add(new IssueItem(issue, Loc.F(issue.TitleKey, args), Loc.F(issue.WhyKey, args), Loc.T($"Category_{issue.Category}"),
                    FixLabelOf(issue.Fix)));
            }

            SubScores.Add(new SubScoreItem(Loc.T("Category_Security"), report.SubScores.Security));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Performance"), report.SubScores.Performance));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Stability"), report.SubScores.Stability));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Storage"), report.SubScores.Storage));
        }

        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(ScoreLabel));
        OnPropertyChanged(nameof(LastAnalysis));
        Main.SetAttentionCount(Issues.Count);
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(PrimaryIsFixAll));
        OnPropertyChanged(nameof(ShowFixAllPremium));
    }
}
