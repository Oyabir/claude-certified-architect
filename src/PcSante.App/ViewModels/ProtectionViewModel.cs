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
public sealed record ProtectionItem(string Label, string Technical, bool Enabled, CommandId? Action, IReadOnlyDictionary<string, string>? Parameters = null)
{
    public string State => Enabled ? Loc.T("Common_On") : Loc.T("Common_Off");

    public bool HasAction => Action is not null;

    public string ActionLabel => Enabled ? Loc.T("Common_Disable") : Loc.T("Common_Enable");
}

public sealed record ThreatItem(string Name, string Date, string Status, string? Resource);

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
        _ => Loc.F("Protection_DefenderSummary", Loc.Date(Defender.SignaturesUpdatedAt), Loc.Date(Max(Defender.LastQuickScanAt, Defender.LastFullScanAt))),
    };

    public bool ShowTamperNote => Defender?.TamperProtectionEnabled == true;

    public bool IsAdvanced => !Main.IsSimpleMode;

    public override async Task LoadAsync()
    {
        Defender = await Query<DefenderStatus>(CommandId.GetDefenderStatus).ConfigureAwait(true);
        Protections.Clear();
        if (Defender is { IsAvailable: true })
        {
            Protections.Add(new ProtectionItem(Loc.T("Protection_Realtime"), Loc.T("Protection_RealtimeTech"), Defender.RealTimeProtectionEnabled, Defender.RealTimeProtectionEnabled ? null : CommandId.EnableRealtimeProtection));
            Protections.Add(new ProtectionItem(Loc.T("Protection_Cloud"), Loc.T("Protection_CloudTech"), Defender.CloudProtectionEnabled, Defender.CloudProtectionEnabled ? null : CommandId.EnableCloudProtection));
            Protections.Add(new ProtectionItem(Loc.T("Protection_Ransomware"), Loc.T("Protection_RansomwareTech"), Defender.ControlledFolderAccessEnabled, Defender.ControlledFolderAccessEnabled ? null : CommandId.EnableControlledFolderAccess));
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
                new Dictionary<string, string> { ["profile"] = profile.Profile.ToString() }));
        }

        Threats.Clear();
        foreach (var t in (await Query<List<ThreatInfo>>(CommandId.GetThreatHistory).ConfigureAwait(true) ?? []).Take(50))
        {
            Threats.Add(new ThreatItem(t.Name, Loc.Date(t.DetectedAt), Loc.T($"Threat_{t.Status}"), t.Resource));
        }

        Quarantine.Clear();
        foreach (var q in await Query<List<QuarantineItem>>(CommandId.GetQuarantine).ConfigureAwait(true) ?? [])
        {
            Quarantine.Add(new QuarantineRow(q.Id, q.ThreatName, Loc.Date(q.QuarantinedAt), q.Path));
        }

        OnPropertyChanged(nameof(DefenderActive));
        OnPropertyChanged(nameof(DefenderSummary));
        OnPropertyChanged(nameof(ShowTamperNote));
        OnPropertyChanged(nameof(IsAdvanced));
    }

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
