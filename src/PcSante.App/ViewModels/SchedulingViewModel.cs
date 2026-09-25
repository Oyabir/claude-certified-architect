using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Scheduling;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

public sealed record Choice(string Value, string Label);

/// <summary>Modèle de tâche planifiée modifiable (jour, heure, conditions).</summary>
public sealed partial class TemplateRow : ObservableObject
{
    public TemplateRow(TemplateView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Id = view.Id;
        Enabled = view.Enabled;
        _day = view.Settings.Monthly ? ScheduleSettings.MonthStart : view.Settings.Day?.ToString() ?? "Everyday";
        _time = view.Settings.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        _onlyWhenIdle = view.Settings.OnlyWhenIdle;
        _onlyOnAcPower = view.Settings.OnlyOnAcPower;
        LastRun = view.LastRun is { } r ? Loc.F(r.Succeeded ? "Scheduling_LastRunOk" : "Scheduling_LastRunFailed", Loc.Date(r.StartedAt)) : Loc.T("Scheduling_NeverRun");
    }

    public ScheduledTemplateId Id { get; }

    public string Label => Loc.T($"Template_{Id}");

    public string Explanation => Loc.T($"Template_{Id}_Why");

    public bool Enabled { get; }

    public string State => Loc.T(Enabled ? "Scheduling_Active" : "Scheduling_Inactive");

    public string ActionLabel => Loc.T(Enabled ? "Scheduling_Save" : "Scheduling_Enable");

    public string LastRun { get; }

    public bool IsWeekly => Id != ScheduledTemplateId.DailyAntivirusScan;

    [ObservableProperty]
    private string _day;

    [ObservableProperty]
    private string _time;

    [ObservableProperty]
    private bool _onlyWhenIdle;

    [ObservableProperty]
    private bool _onlyOnAcPower;

    /// <param name="language">Langue de l'interface : celle des documents produits par la tâche (rapport mensuel).</param>
    public Dictionary<string, string> ToParameters(string language) => new()
    {
        ["template"] = Id.ToString(),
        ["day"] = IsWeekly ? Day : "Everyday",
        ["time"] = Time,
        ["onlyWhenIdle"] = OnlyWhenIdle ? "true" : "false",
        ["onlyOnAcPower"] = OnlyOnAcPower ? "true" : "false",
        ["language"] = language,
    };
}

[SupportedOSPlatform("windows")]
public sealed partial class SchedulingViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Scheduling;

    public ObservableCollection<TemplateRow> Templates { get; } = [];

    public ObservableCollection<string> RunLog { get; } = [];

    public IReadOnlyList<Choice> Days { get; } = CommandDefinitions.Days.Where(d => d != "Everyday").Select(d => new Choice(d, Loc.T($"Day_{d}"))).ToList();

    public IReadOnlyList<string> Times { get; } = Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{(i % 2) * 30:00}").ToList();

    public override async Task LoadAsync()
    {
        Templates.Clear();
        foreach (var t in await Query<List<TemplateView>>(CommandId.GetScheduledTemplates).ConfigureAwait(true) ?? [])
        {
            Templates.Add(new TemplateRow(t));
        }

        RunLog.Clear();
        foreach (var r in (await Query<List<TaskRunEntry>>(CommandId.GetTaskRunLog).ConfigureAwait(true) ?? []).Take(50))
        {
            RunLog.Add(Loc.F("Scheduling_LogLine", Loc.Date(r.StartedAt), Loc.T($"Template_{r.Template}"), Loc.T(r.MessageKey)));
        }
    }

    /// <summary>Bouton principal : active les 3 tâches recommandées en un clic.</summary>
    [RelayCommand]
    private async Task EnableRecommendedAsync()
    {
        if (!await Infrastructure.Dialogs.ConfirmAsync(Loc.T("Scheduling_EnableAll"), Loc.T("Scheduling_EnableAllConfirm"), Loc.T("Scheduling_EnableAll")).ConfigureAwait(true))
        {
            return;
        }

        await EnableAllAsync(this, Main.Settings.Language).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task SaveAsync(TemplateRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.EnableScheduledTemplate, row.ToParameters(Main.Settings.Language));
    }

    [RelayCommand]
    private Task DisableAsync(TemplateRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.DisableScheduledTemplate, new Dictionary<string, string> { ["template"] = row.Id.ToString() });
    }

    /// <summary>Active les 3 modèles recommandés avec leurs réglages par défaut (les autres restent au choix).</summary>
    public static async Task<bool> EnableAllAsync(PageViewModel page, string language)
    {
        ArgumentNullException.ThrowIfNull(page);
        var ok = true;
        foreach (var template in ScheduledTemplates.Recommended)
        {
            var row = new TemplateRow(new TemplateView(template.Id, false, template.Default, template.Commands, null));
            var result = await page.RunForOtherAsync(CommandId.EnableScheduledTemplate, row.ToParameters(language)).ConfigureAwait(true);
            ok &= result.IsSuccess;
            if (!result.IsSuccess)
            {
                page.Message.Show(result);
                return false;
            }
        }

        page.Message.Show(Loc.T("Scheduling_AllEnabled"), Infrastructure.MessageKind.Success);
        await page.LoadAsync().ConfigureAwait(true);
        return ok;
    }
}
