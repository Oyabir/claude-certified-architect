using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

/// <summary>Base des écrans : exécution d'une commande avec confirmation, indicateur d'activité et message de résultat.</summary>
[SupportedOSPlatform("windows")]
public abstract partial class PageViewModel(MainViewModel main) : ObservableObject
{
    protected MainViewModel Main { get; } = main;

    protected ServiceClient Service => Main.Service;

    public ResultMessage Message => Main.Message;

    public abstract ScreenId Screen { get; }

    public string Title => Loc.T(Main.IsSimpleMode && Screen == ScreenId.Optimization ? "Nav_Optimization_Simple" : $"Nav_{Screen}");

    public string Subtitle => Loc.T($"Subtitle_{Screen}");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    public abstract Task LoadAsync();

    /// <summary>
    /// Exécute une commande : confirmation si le catalogue l'exige, attente visible, message clair, rafraîchissement.
    /// </summary>
    protected async Task<CommandResult?> ExecuteAsync(
        CommandId command,
        IReadOnlyDictionary<string, string>? parameters = null,
        string? confirmSubject = null,
        bool reload = true,
        string? busyKey = null)
    {
        // Offre Gratuite : pas de confirmation suivie d'un refus, on explique tout de suite.
        if (Main.NeedsPremium(command))
        {
            Main.ShowPremiumRequired();
            return null;
        }

        var descriptor = CommandDefinitions.Get(command);
        if (descriptor.RequiresConfirmation && !await Dialogs.ConfirmCommandAsync(command, confirmSubject).ConfigureAwait(true))
        {
            return null;
        }

        IsBusy = true;
        BusyText = Loc.T(busyKey ?? "Common_Working");
        try
        {
            var result = await Service.RunAsync(command, parameters, descriptor.RequiresConfirmation).ConfigureAwait(true);
            Message.Show(result);
            if (result.Reason == FailureReason.ServiceUnavailable)
            {
                Main.ServiceAvailable = false;
            }

            if (reload && result.Reason != FailureReason.ServiceUnavailable)
            {
                await LoadAsync().ConfigureAwait(true);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Envoi direct sans confirmation ni rechargement (enchaînements maîtrisés).</summary>
    public Task<CommandResult> RunForOtherAsync(CommandId command, IReadOnlyDictionary<string, string>? parameters = null) =>
        Service.RunAsync(command, parameters);

    protected async Task<T?> Query<T>(CommandId command, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var result = await Service.RunAsync(command, parameters).ConfigureAwait(true);
        if (result.Reason == FailureReason.ServiceUnavailable)
        {
            Main.ServiceAvailable = false;
            return default;
        }

        Main.ServiceAvailable = true;
        return result.IsSuccess ? result.GetData<T>() : default;
    }
}
