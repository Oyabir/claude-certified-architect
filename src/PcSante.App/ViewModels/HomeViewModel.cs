using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Scheduling;
using PcSante.Core.Ui;
using PcSante.Core.Windows;
using PcSante.Reporting;

namespace PcSante.App.ViewModels;

/// <summary>Problème affiché : ce qui ne va pas, pourquoi c'est important, bouton au verbe explicite.</summary>
public sealed record IssueItem(HealthIssue Issue, string Title, string Why, string Category, string FixLabel)
{
    public IssueSeverity Severity => Issue.Severity;

    public bool CanFix => Issue.Fix is not null;

    /// <summary>Couleur de la ligne : rouge (à corriger), orange (à surveiller), marque (conseil).</summary>
    public Tone Tone => Severity switch
    {
        IssueSeverity.Critical => Tone.Critical,
        IssueSeverity.Warning => Tone.Warn,
        _ => Tone.Brand,
    };

    /// <summary>Étiquette « Important » (critique ou à surveiller) ou « Conseillé » (information).</summary>
    public string Tag => Loc.T(Severity == IssueSeverity.Info ? "Tag_Advised" : "Tag_Important");

    public string Icon => Issue.Code switch
    {
        "LowDiskSpace" => "PcsIcon.hard-drive",
        "CleanableFiles" => "PcsIcon.trash",
        "ManyStartupPrograms" => "PcsIcon.zap",
        "HighMemory" => "PcsIcon.memory",
        "RecentCrashes" => "PcsIcon.alert-triangle",
        "UpdatesBroken" or "UpdatesNotChecked" or "SignaturesOutdated" => "PcsIcon.download",
        "RestoreDisabled" or "NoRecentRestorePoint" => "PcsIcon.restore",
        "GuestEnabled" or "NewRemoteConnection" => "PcsIcon.users",
        "RebootPending" => "PcsIcon.refresh",
        _ => Issue.Category switch
        {
            HealthCategory.Security => "PcsIcon.shield",
            HealthCategory.Performance => "PcsIcon.pulse",
            HealthCategory.Stability => "PcsIcon.monitor",
            _ => "PcsIcon.hard-drive",
        },
    };
}

public sealed record SubScoreItem(string Label, int Value, string Icon);

/// <summary>Entrée de l'activité récente (journal des actions).</summary>
public sealed record ActivityItem(string Label, string When, bool Succeeded);

[SupportedOSPlatform("windows")]
public sealed partial class HomeViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Home;

    [ObservableProperty]
    private HealthReport? _report;

    public ObservableCollection<IssueItem> Issues { get; } = [];

    public ObservableCollection<SubScoreItem> SubScores { get; } = [];

    /// <summary>« Tout le reste va bien » : catégories sans aucun problème détecté.</summary>
    public ObservableCollection<string> AllGood { get; } = [];

    public ObservableCollection<ActivityItem> Activity { get; } = [];

    [ObservableProperty]
    private string _machineLine = string.Empty;

    [ObservableProperty]
    private bool _watchActive;

    public string WatchStatus => Loc.T(WatchActive ? "Watch_Active" : "Watch_Inactive");

    public bool HasActivity => Activity.Count > 0;

    public bool HasAllGood => AllGood.Count > 0;

    public int? ScoreValue => Report?.Score;

    public Tone ScoreTone => HealthBrushes.ToneOf(Color);

    /// <summary>En-tête : « Dernière analyse aujourd'hui à 15:20 · nom du PC · édition de Windows ».</summary>
    public string HeaderLine => string.Join(" · ", new[]
    {
        Report is null ? Loc.T("Home_NoAnalysis") : Loc.F("Home_LastAnalysisWhen", Loc.When(Report.AnalyzedAt)),
        MachineLine,
    }.Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>Phrase de conclusion, construite à partir des problèmes réellement détectés.</summary>
    public string Conclusion => Report is null ? Loc.T("Home_NoAnalysis") : ConclusionFor(Report);

    /// <summary>Phrase de conclusion d'un rapport (Accueil et premier lancement).</summary>
    public static string ConclusionFor(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Issues.Count == 0)
        {
            return Loc.T("Home_Hero_AllGood");
        }

        var categories = report.Issues.Select(i => i.Category).Distinct().ToList();
        if (categories.Contains(HealthCategory.Security))
        {
            return Loc.T("Home_Hero_Security");
        }

        return categories.Count == 1
            ? Loc.F("Home_Hero_ProtectedOne", Loc.T($"Category_{categories[0]}_Subject"))
            : Loc.T("Home_Hero_ProtectedMany");
    }

    /// <summary>« 2 points à regarder · rien n'est supprimé sans votre accord. » (durée estimée non fournie : masquée).</summary>
    public string Summary => Issues.Count switch
    {
        0 => string.Empty,
        1 => Loc.T("Home_Summary_One"),
        _ => Loc.F("Home_Summary_Many", Issues.Count),
    };

    public string IssuesCount => Issues.Count switch
    {
        0 => string.Empty,
        1 => Loc.T("Home_Points_One"),
        _ => Loc.F("Home_Points_Many", Issues.Count),
    };

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
        await LoadSidePanelsAsync().ConfigureAwait(true);
    }

    /// <summary>Données d'affichage déjà disponibles : nom du PC, veille automatique, activité récente.</summary>
    private async Task LoadSidePanelsAsync()
    {
        if (!Main.ServiceAvailable)
        {
            return;
        }

        if (await Query<SystemInfo>(CommandId.GetSystemInfo).ConfigureAwait(true) is { } info)
        {
            MachineLine = string.Join(" · ", new[] { info.MachineName, info.ProductName }.Where(t => !string.IsNullOrWhiteSpace(t)));
            OnPropertyChanged(nameof(HeaderLine));
        }

        var templates = await Query<List<TemplateView>>(CommandId.GetScheduledTemplates).ConfigureAwait(true) ?? [];
        WatchActive = ScheduledTemplates.Recommended.All(r => templates.Any(t => t.Id == r.Id && t.Enabled));
        OnPropertyChanged(nameof(WatchStatus));

        Activity.Clear();
        foreach (var a in (await Query<List<AuditEntry>>(CommandId.GetAuditLog).ConfigureAwait(true) ?? [])
                     .OrderByDescending(a => a.Timestamp).Take(3))
        {
            var succeeded = a.Outcome is AuditOutcome.Succeeded or AuditOutcome.AlreadyDone or AuditOutcome.Started;
            var when = Loc.Capitalize(Loc.When(a.Timestamp));
            Activity.Add(new ActivityItem(ReportPdfBuilder.CommandLabel(a.Command, Loc.T),
                succeeded ? when : when + " · " + Loc.T($"Outcome_{a.Outcome}"), succeeded));
        }

        OnPropertyChanged(nameof(HasActivity));
    }

    /// <summary>« Activer les 3 tâches » : même action que le bouton principal de Planification.</summary>
    [RelayCommand]
    private async Task EnableWatchAsync()
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Scheduling_EnableAll"), Loc.T("Scheduling_EnableAllConfirm"), Loc.T("Scheduling_EnableAll")).ConfigureAwait(true))
        {
            return;
        }

        await SchedulingViewModel.EnableAllAsync(this, Main.Settings.Language).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowActivity() => Main.Navigate(ScreenId.Reports);

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

        // Récapitulatif : une case par correction, seules les cases cochées sont exécutées.
        var chosen = await Dialogs.ChooseAsync(Loc.T("Home_FixAll"), Loc.T("Home_FixAllIntro"),
            fixable.Select(i => i.FixLabel + " — " + i.Title).ToList(), Loc.T("Home_FixAll")).ConfigureAwait(true);
        if (chosen is null || chosen.Count == 0)
        {
            return;
        }

        fixable = chosen.Select(index => fixable[index]).ToList();
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

    /// <summary>Libellé au verbe explicite (jamais « Voir » seul).</summary>
    private string FixLabelOf(HealthIssue issue)
    {
        if (issue.Fix?.Command is not { } command)
        {
            var screen = issue.Fix?.Screen ?? ScreenId.Home;
            var specific = Loc.T($"Fix_Open_{issue.Code}");
            return specific != $"Fix_Open_{issue.Code}" ? specific : Loc.T($"Fix_Open_{screen}");
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
                // Nombres au format de la langue (28,7 Go et non 28.7 Go).
                var args = issue.Args.Select(Loc.Number).ToArray();
                Issues.Add(new IssueItem(issue, Loc.F(issue.TitleKey, args), Loc.F(issue.WhyKey, args), Loc.T($"Category_{issue.Category}"),
                    FixLabelOf(issue)));
            }

            SubScores.Add(new SubScoreItem(Loc.T("Category_Security"), report.SubScores.Security, "PcsIcon.shield"));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Performance"), report.SubScores.Performance, "PcsIcon.pulse"));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Stability"), report.SubScores.Stability, "PcsIcon.check-circle"));
            SubScores.Add(new SubScoreItem(Loc.T("Category_Storage"), report.SubScores.Storage, "PcsIcon.hard-drive"));
        }

        AllGood.Clear();
        if (report is not null)
        {
            foreach (var category in Enum.GetValues<HealthCategory>().Where(c => Issues.All(i => i.Issue.Category != c)))
            {
                AllGood.Add(Loc.T($"Home_AllGood_{category}"));
            }
        }

        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(ScoreValue));
        OnPropertyChanged(nameof(ScoreTone));
        OnPropertyChanged(nameof(HeaderLine));
        OnPropertyChanged(nameof(Conclusion));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IssuesCount));
        OnPropertyChanged(nameof(HasAllGood));
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
