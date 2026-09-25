using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

/// <summary>Ligne d'état « protection » : libellé simple, terme technique en petit, état, action.</summary>
public sealed record ProtectionItem(string Label, string Technical, bool Enabled, CommandId? Action, IReadOnlyDictionary<string, string>? Parameters = null, string Icon = "PcsIcon.shield")
{
    public string State => Enabled ? Loc.T("Common_On") : Loc.T("Common_Off");

    public bool HasAction => Action is not null;

    public string ActionLabel => Enabled ? Loc.T("Common_Disable") : Loc.T("Common_Enable");

    /// <summary>Nom lu par le Narrateur sur l'interrupteur : « Réseau domestique : Activée ».</summary>
    public string SwitchName => Label + " : " + State;

    /// <summary>Infobulle d'un interrupteur non modifiable (désactivation réservée au mode Avancé).</summary>
    public string? LockedReason => HasAction ? null : Loc.T("Protection_DisableAdvancedOnly");
}

/// <summary>Menace : nom, chemin raccourci (complet en infobulle), date, traitée ou non.</summary>
public sealed record ThreatItem(string Name, string Date, string Status, string? Resource, bool Handled)
{
    public string ShortResource => ProtectionViewModel.ShortenPath(Resource);
}

public sealed record QuarantineRow(string Id, string Name, string Date, string? Path);

[SupportedOSPlatform("windows")]
public sealed partial class ProtectionViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Protection;

    [ObservableProperty]
    private DefenderStatus? _defender;

    public ObservableCollection<ProtectionItem> Protections { get; } = [];

    public ObservableCollection<ProtectionItem> Firewall { get; } = [];

    public ObservableCollection<ProtectionItem> AntivirusProducts { get; } = [];

    public ObservableCollection<ThreatItem> Threats { get; } = [];

    public ObservableCollection<QuarantineRow> Quarantine { get; } = [];

    public bool DefenderActive => Defender is { IsAvailable: true, IsActiveAntivirus: true };

    public string DefenderSummary => Defender switch
    {
        { OtherActiveAntivirus.Count: > 0 } when !DefenderActive => Loc.F("Protection_OtherAntivirusNamed", string.Join(", ", Defender.OtherActiveAntivirus)),
        { OtherActiveAntivirus.Count: 0 } when !DefenderActive => Loc.T("Protection_NoAntivirus"),
        null or { IsAvailable: false } => Loc.T("Protection_DefenderUnavailable"),
        { IsActiveAntivirus: false } => Loc.T("Protection_OtherAntivirus"),
        _ => Loc.F("Protection_DefenderSummary", Loc.When(Defender.SignaturesUpdatedAt), Loc.When(Max(Defender.LastQuickScanAt, Defender.LastFullScanAt))),
    };

    public bool ShowTamperNote => Defender?.TamperProtectionEnabled == true;

    /// <summary>Protections recommandées encore désactivées (antivirus et pare-feu).</summary>
    public int Recommendations => Protections.Count(p => !p.Enabled) + Firewall.Count(p => !p.Enabled);

    /// <summary>Bandeau d'état : vert (protégé), orange (recommandations), rouge (antivirus absent ou surveillance coupée).</summary>
    public Tone StatusTone =>
        Defender is null ? Tone.Neutral
        : (!DefenderActive && Defender.OtherActiveAntivirus is not { Count: > 0 }) || (DefenderActive && !Defender.RealTimeProtectionEnabled) ? Tone.Critical
        : Recommendations > 0 ? Tone.Warn
        : Tone.Good;

    public string StatusTitle => Loc.T(StatusTone switch
    {
        Tone.Good => "Protection_StatusGood",
        Tone.Warn => "Protection_StatusWarn",
        Tone.Critical => "Protection_StatusCritical",
        _ => "Protection_StatusUnknown",
    });

    public string RecommendationsText => Recommendations switch
    {
        0 => string.Empty,
        1 => Loc.T("Protection_Recommendation_One"),
        _ => Loc.F("Protection_Recommendation_Many", Recommendations),
    };

    public bool HasRecommendations => Recommendations > 0;

    /// <summary>Résumé des antivirus déclarés : « Windows Defender · à jour » ou « 2 antivirus détectés ».</summary>
    public string AntivirusHeader => AntivirusProducts.Count switch
    {
        0 => string.Empty,
        1 => AntivirusProducts[0].Label + " · " + AntivirusProducts[0].Technical,
        _ => Loc.F("Protection_AvCount", AntivirusProducts.Count),
    };

    public bool HasSeveralAntivirus => AntivirusProducts.Count > 1;

    [ObservableProperty]
    private bool _showAntivirusList;

    public int ThreatsToHandle => Threats.Count(t => !t.Handled);

    public string ThreatsTag => Threats.Count == 0 ? string.Empty
        : ThreatsToHandle == 0 ? Loc.T("Protection_ThreatsAllHandled") : Loc.F("Protection_ThreatsToHandle", ThreatsToHandle);

    public Tone ThreatsTone => ThreatsToHandle == 0 ? Tone.Good : Tone.Critical;

    public string QuarantineSummary => Quarantine.Count == 0 ? Loc.T("Protection_QuarantineEmptyShort") : Loc.F("Protection_QuarantineCount", Quarantine.Count);

    public bool HasThreats => Threats.Count > 0;

    public bool HasQuarantine => Quarantine.Count > 0;

    /// <summary>« Téléchargements › fichier.exe » : dossier connu traduit + nom du fichier ; le chemin complet reste en infobulle.</summary>
    public static string ShortenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var clean = path.Contains("->", StringComparison.Ordinal) ? path[(path.LastIndexOf("->", StringComparison.Ordinal) + 2)..] : path;
        clean = clean.Replace("file:_", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var parts = clean.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return clean;
        }

        var folder = parts[^2];
        var known = folder.ToUpperInvariant() switch
        {
            "DOWNLOADS" or "TÉLÉCHARGEMENTS" => Loc.T("Folder_Downloads"),
            "DESKTOP" or "BUREAU" => Loc.T("Folder_Desktop"),
            "DOCUMENTS" => Loc.T("Folder_Documents"),
            "TEMP" or "TMP" => Loc.T("Folder_Temp"),
            _ => folder,
        };
        return known + " › " + parts[^1];
    }

    public bool IsAdvanced => !Main.IsSimpleMode;

    public override async Task LoadAsync()
    {
        Defender = await Query<DefenderStatus>(CommandId.GetDefenderStatus).ConfigureAwait(true);
        Protections.Clear();
        if (Defender is { IsAvailable: true })
        {
            Protections.Add(new ProtectionItem(Loc.T("Protection_Realtime"), Loc.T("Protection_RealtimeDesc"), Defender.RealTimeProtectionEnabled, Defender.RealTimeProtectionEnabled ? null : CommandId.EnableRealtimeProtection));
            Protections.Add(new ProtectionItem(Loc.T("Protection_Cloud"), Loc.T("Protection_CloudDesc"), Defender.CloudProtectionEnabled, Defender.CloudProtectionEnabled ? null : CommandId.EnableCloudProtection));
            Protections.Add(new ProtectionItem(Loc.T("Protection_Ransomware"), Loc.T("Protection_RansomwareDesc"), Defender.ControlledFolderAccessEnabled, Defender.ControlledFolderAccessEnabled ? null : CommandId.EnableControlledFolderAccess));
        }

        // Antivirus déclarés à Windows (Defender et antivirus tiers) : consultation seulement.
        AntivirusProducts.Clear();
        foreach (var av in await Query<List<AntivirusProduct>>(CommandId.GetAntivirusProducts).ConfigureAwait(true) ?? [])
        {
            AntivirusProducts.Add(new ProtectionItem(av.Name, Loc.T(av.UpToDate ? "Protection_AvUpToDate" : "Protection_AvOutdated"), av.Enabled, null));
        }

        Firewall.Clear();
        foreach (var profile in await Query<List<FirewallProfileStatus>>(CommandId.GetFirewallStatus).ConfigureAwait(true) ?? [])
        {
            Firewall.Add(new ProtectionItem(Loc.T($"Firewall_{profile.Profile}"), Loc.T($"Firewall_{profile.Profile}Tech"), profile.Enabled,
                // Désactiver une protection n'est proposé qu'en mode Avancé.
                profile.Enabled ? (Main.IsSimpleMode ? null : CommandId.DisableFirewallProfile) : CommandId.EnableFirewallProfile,
                new Dictionary<string, string> { ["profile"] = profile.Profile.ToString() },
                profile.Profile switch
                {
                    FirewallProfile.Domain => "PcsIcon.building",
                    FirewallProfile.Private => "PcsIcon.home",
                    _ => "PcsIcon.wifi",
                }));
        }

        Threats.Clear();
        foreach (var t in (await Query<List<ThreatInfo>>(CommandId.GetThreatHistory).ConfigureAwait(true) ?? []).Take(50))
        {
            Threats.Add(new ThreatItem(t.Name, Loc.Date(t.DetectedAt), Loc.T($"Threat_{t.Status}"), t.Resource,
                !string.Equals(t.Status, "ActionRequired", StringComparison.OrdinalIgnoreCase)));
        }

        Quarantine.Clear();
        foreach (var q in await Query<List<QuarantineItem>>(CommandId.GetQuarantine).ConfigureAwait(true) ?? [])
        {
            Quarantine.Add(new QuarantineRow(q.Id, q.ThreatName, Loc.Date(q.QuarantinedAt), q.Path));
        }

        foreach (var name in new[]
                 {
                     nameof(Recommendations), nameof(StatusTone), nameof(StatusTitle), nameof(RecommendationsText), nameof(HasRecommendations),
                     nameof(AntivirusHeader), nameof(HasSeveralAntivirus), nameof(ThreatsToHandle), nameof(ThreatsTag), nameof(ThreatsTone),
                     nameof(QuarantineSummary), nameof(HasThreats), nameof(HasQuarantine),
                 })
        {
            OnPropertyChanged(name);
        }

        OnPropertyChanged(nameof(DefenderActive));
        OnPropertyChanged(nameof(DefenderSummary));
        OnPropertyChanged(nameof(ShowTamperNote));
        OnPropertyChanged(nameof(IsAdvanced));
    }

    [RelayCommand]
    private void ToggleAntivirusList() => ShowAntivirusList = !ShowAntivirusList;

    [RelayCommand]
    private Task QuickScanAsync() => ExecuteAsync(CommandId.StartQuickScan);

    [RelayCommand]
    private Task FullScanAsync() => ExecuteAsync(CommandId.StartFullScan);

    [RelayCommand]
    private async Task CustomScanAsync()
    {
        var folder = Dialogs.PickFolder();
        if (folder is not null)
        {
            await ExecuteAsync(CommandId.StartCustomScan, new Dictionary<string, string> { ["path"] = folder }).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task UpdateSignaturesAsync() => ExecuteAsync(CommandId.UpdateSignatures, busyKey: "Protection_Updating");

    [RelayCommand]
    private async Task ToggleAsync(ProtectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Action is { } command)
        {
            await ExecuteAsync(command, item.Parameters, item.Label).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task RestoreAsync(QuarantineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.RestoreQuarantinedItem, new Dictionary<string, string> { ["id"] = row.Id }, row.Name);
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
