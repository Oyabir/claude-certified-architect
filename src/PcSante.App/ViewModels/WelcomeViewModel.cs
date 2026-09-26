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
        if (Language != _main.Settings.Language)
        {
            // La langue change : l'interface est rechargée et l'assistant reprend à l'étape 2.
            MainViewModel.PendingWelcomeStep = 2;
            _main.SaveSettings(_main.Settings with { Language = Language }, restartUi: true);
            return;
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

    [RelayCommand]
    private void Finish() => _main.CompleteWelcome();

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
