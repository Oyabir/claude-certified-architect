using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Licensing;

namespace PcSante.App.ViewModels;

/// <summary>
/// Premier lancement en 3 étapes (section 11) : langue, activation de la licence, première analyse lancée
/// automatiquement ; puis proposition d'activer les 3 tâches recommandées en un clic.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WelcomeViewModel(MainViewModel main) : ObservableObject
{
    private readonly MainViewModel _main = main;

    public ResultMessage Message => _main.Message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanguageStep), nameof(IsLicenseStep), nameof(IsAnalysisStep), nameof(StepText))]
    private int _step = 1;

    [ObservableProperty]
    private string _language = main.Settings.Language;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyLooksValid))]
    private string _key = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private HealthReport? _report;

    /// <summary>La première analyse a échoué (service absent ou en démarrage) : proposer de réessayer ou de continuer.</summary>
    [ObservableProperty]
    private bool _analysisFailed;

    [ObservableProperty]
    private bool _tasksEnabled;

    /// <summary>
    /// Interrupteur « Laisser PC Santé veiller automatiquement » : désactivé par défaut (décision du commanditaire) ;
    /// les 3 tâches ne sont créées à la fin que si l'utilisateur l'a activé.
    /// </summary>
    [ObservableProperty]
    private bool _watchRequested;

    /// <summary>Choix du mode à l'étape 1 : écrit le même réglage que « Mode Avancé » dans Paramètres.</summary>
    [ObservableProperty]
    private bool _advancedMode = main.Settings.Mode == Core.Ui.DisplayMode.Advanced;

    public bool SimpleMode
    {
        get => !AdvancedMode;
        set => AdvancedMode = !value;
    }

    partial void OnAdvancedModeChanged(bool value) => OnPropertyChanged(nameof(SimpleMode));

    /// <summary>La langue choisie s'applique immédiatement (textes et sens de lecture) : l'assistant reste à l'étape 1.</summary>
    partial void OnLanguageChanged(string value)
    {
        if (value != _main.Settings.Language)
        {
            MainViewModel.PendingWelcomeStep = 1;
            _main.SaveSettings(_main.Settings with { Language = value }, restartUi: true);
        }
    }

    public int? ScoreValue => Report?.Score;

    public Tone ScoreTone => Report is null ? Tone.Neutral : HealthBrushes.ToneOf(Report.Color);

    public string ScoreLabel => Report is null ? string.Empty : Loc.T($"Color_{Report.Color}");

    public string Conclusion => Report is null ? string.Empty : HomeViewModel.ConclusionFor(Report);

    /// <summary>Légende du score, tirée des seuils du code (vert ≥ 80, orange ≥ 50).</summary>
    public string RangeGood => Loc.F("Score_Range", HealthScoreCalculator.GreenThreshold, 100);

    public string RangeWarn => Loc.F("Score_Range", HealthScoreCalculator.OrangeThreshold, HealthScoreCalculator.GreenThreshold - 1);

    public string RangeCritical => Loc.F("Score_Range", 0, HealthScoreCalculator.OrangeThreshold - 1);

    public bool StepTwoReached => Step >= 2;

    public bool StepThreeReached => Step >= 3;

    partial void OnReportChanged(HealthReport? value)
    {
        OnPropertyChanged(nameof(ScoreValue));
        OnPropertyChanged(nameof(ScoreTone));
        OnPropertyChanged(nameof(ScoreLabel));
        OnPropertyChanged(nameof(Conclusion));
    }

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(StepTwoReached));
        OnPropertyChanged(nameof(StepThreeReached));
    }

    public string ProductName => Core.ProductInfo.Name;

    /// <summary>Retour : l'étape « licence » est sautée si la licence est déjà active.</summary>
    [RelayCommand]
    private void Back()
    {
        if (Step > 1)
        {
            Step = Step == 3 && _main.IsPremium ? 1 : Step - 1;
        }
    }

    public IReadOnlyList<Choice> Languages { get; } = [new("fr", "Français"), new("en", "English"), new("ar", "العربية")];

    public bool IsLanguageStep => Step == 1;

    public bool IsLicenseStep => Step == 2;

    public bool IsAnalysisStep => Step == 3;

    public string StepText => Loc.F("Welcome_Step", Step, 3);

    public bool KeyLooksValid => LicenseKeyFormat.IsWellFormed(Key);

    public bool CanEnableTasks => Report is not null && _main.IsPremium && !TasksEnabled;

    [RelayCommand]
    private async Task ChooseLanguageAsync()
    {
        var mode = AdvancedMode ? Core.Ui.DisplayMode.Advanced : Core.Ui.DisplayMode.Simple;
        if (mode != _main.Settings.Mode)
        {
            _main.SaveSettings(_main.Settings with { Mode = mode }, restartUi: false);
        }

        Step = 2;
        await SkipLicenseIfActiveAsync().ConfigureAwait(true);
    }

    /// <summary>Licence déjà active (réinstallation, version de test) : l'étape « licence » est sautée.</summary>
    public async Task SkipLicenseIfActiveAsync()
    {
        if (Step == 2 && _main.IsPremium)
        {
            await StartAnalysisAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _main.Service.RunAsync(CommandId.ActivateLicense, new Dictionary<string, string> { ["key"] = Key }).ConfigureAwait(true);
            Message.Show(result);
            await _main.RefreshLicenseAsync().ConfigureAwait(true);
            if (result.IsSuccess)
            {
                await StartAnalysisAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Continuer en offre Gratuite : l'analyse et le score restent disponibles.</summary>
    [RelayCommand]
    private Task SkipLicenseAsync() => StartAnalysisAsync();

    [RelayCommand]
    private async Task EnableTasksAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var template in Core.Scheduling.ScheduledTemplates.Recommended)
            {
                var row = new TemplateRow(new Core.Scheduling.TemplateView(template.Id, false, template.Default, template.Commands, null));
                var result = await _main.Service.RunAsync(CommandId.EnableScheduledTemplate, row.ToParameters(_main.Settings.Language)).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    Message.Show(result);
                    return;
                }
            }

            TasksEnabled = true;
            OnPropertyChanged(nameof(CanEnableTasks));
            Message.Show(Loc.T("Scheduling_AllEnabled"), MessageKind.Success);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>« Voir mon tableau de bord » : crée les 3 tâches si l'interrupteur a été activé, puis ouvre l'Accueil.</summary>
    [RelayCommand]
    private async Task FinishAsync()
    {
        if (WatchRequested && CanEnableTasks)
        {
            await EnableTasksAsync().ConfigureAwait(true);
        }

        _main.CompleteWelcome();
    }

    [RelayCommand]
    private Task RetryAnalysisAsync() => StartAnalysisAsync();

    private async Task StartAnalysisAsync()
    {
        Step = 3;
        AnalysisFailed = false;
        IsBusy = true;
        try
        {
            var result = await _main.Service.RunAsync(CommandId.RunHealthAnalysis).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                Report = result.GetData<HealthReport>();
            }
            else
            {
                Message.Show(result);
            }

            AnalysisFailed = Report is null;

            OnPropertyChanged(nameof(CanEnableTasks));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
