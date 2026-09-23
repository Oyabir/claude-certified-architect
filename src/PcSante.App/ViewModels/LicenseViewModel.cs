using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Licensing;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

/// <summary>Licence : activation par clé, transfert vers ce PC, désactivation de ce PC.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class LicenseViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.License;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyLooksValid))]
    private string _key = string.Empty;

    [ObservableProperty]
    private bool _offerTransfer;

    public LicenseStatus? Status => Main.License;

    public bool KeyLooksValid => LicenseKeyFormat.IsWellFormed(Key);

    public bool IsActive => Status?.State == LicenseState.Active;

    public string Offer => Main.LicenseBadge;

    public string StateText => Status is null ? Loc.T("License_State_Unknown") : Loc.T($"License_State_{Status.State}");

    public string Details => Status is not { State: LicenseState.Active } s ? string.Empty
        : Loc.F("License_Details", s.KeyHint ?? "", s.LicenseExpiresAt is { } e ? Loc.Date(e) : Loc.T("License_NoEnd"), Loc.Date(s.LastValidatedAt));

    public override async Task LoadAsync()
    {
        await Main.RefreshLicenseAsync().ConfigureAwait(true);
        Refresh();
    }

    /// <summary>Bouton principal : activer la clé saisie.</summary>
    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (!KeyLooksValid)
        {
            Message.Show(Loc.T("License_InvalidKey"), MessageKind.Error);
            return;
        }

        var result = await ExecuteAsync(CommandId.ActivateLicense, new Dictionary<string, string> { ["key"] = Key }, reload: false, busyKey: "License_Activating").ConfigureAwait(true);
        OfferTransfer = result?.Reason == FailureReason.LicenseUsedElsewhere;
        await LoadAsync().ConfigureAwait(true);
        if (result?.IsSuccess == true)
        {
            Key = string.Empty;
        }
    }

    /// <summary>« Transférer ma licence » : désactive l'ancien PC et active celui-ci (2 fois par an).</summary>
    [RelayCommand]
    private async Task TransferAsync()
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("License_Transfer"), Loc.T("License_TransferConfirm"), Loc.T("License_Transfer")).ConfigureAwait(true))
        {
            return;
        }

        await ExecuteAsync(CommandId.TransferLicense, new Dictionary<string, string> { ["key"] = Key }, reload: false, busyKey: "License_Activating").ConfigureAwait(true);
        OfferTransfer = false;
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        await ExecuteAsync(CommandId.DeactivateLicense, reload: false).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(Offer));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Details));
    }
}
