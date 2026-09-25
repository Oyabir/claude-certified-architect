using CommunityToolkit.Mvvm.ComponentModel;
using PcSante.App.Localization;
using PcSante.Core.Commands;

namespace PcSante.App.Infrastructure;

public enum MessageKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Message de résultat affiché après chaque action (section 11) : phrase claire, jamais de code brut ;
/// le détail technique reste accessible derrière « Détails ».
/// </summary>
public sealed partial class ResultMessage : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private MessageKind _kind;

    [ObservableProperty]
    private string? _details;

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private bool _suggestPremium;

    public Wpf.Ui.Controls.InfoBarSeverity Severity => Kind switch
    {
        MessageKind.Success => Wpf.Ui.Controls.InfoBarSeverity.Success,
        MessageKind.Warning => Wpf.Ui.Controls.InfoBarSeverity.Warning,
        MessageKind.Error => Wpf.Ui.Controls.InfoBarSeverity.Error,
        _ => Wpf.Ui.Controls.InfoBarSeverity.Informational,
    };

    /// <summary>Couleur du message : vert (réussi), marque (information), orange (attention), rouge (échec).</summary>
    public Tone Tone => Kind switch
    {
        MessageKind.Success => Tone.Good,
        MessageKind.Warning => Tone.Warn,
        MessageKind.Error => Tone.Critical,
        _ => Tone.Brand,
    };

    public bool IsSuccess => Kind == MessageKind.Success;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public void Show(string text, MessageKind kind, string? details = null)
    {
        Text = text;
        Kind = kind;
        Details = details;
        ShowDetails = false;
        SuggestPremium = false;
        IsOpen = true;
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(HasDetails));
    }

    public void Show(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var kind = result.Status switch
        {
            CommandStatus.Succeeded or CommandStatus.Started => MessageKind.Success,
            CommandStatus.AlreadyDone => MessageKind.Info,
            CommandStatus.Refused when result.Reason is FailureReason.LicenseRequired => MessageKind.Warning,
            _ => MessageKind.Error,
        };
        var text = Describe(result);
        var details = result.Reason == FailureReason.None && result.TechnicalDetails is null
            ? null
            : string.Join(Environment.NewLine, new[] { $"{result.Reason} · {result.MessageKey}", result.TechnicalDetails }.Where(s => !string.IsNullOrEmpty(s)));
        Show(text, kind, details);
        SuggestPremium = result.Reason == FailureReason.LicenseRequired;
    }

    /// <summary>Phrase claire pour un résultat, avec mise en forme des tailles (octets) le cas échéant.</summary>
    public static string Describe(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var args = result.MessageArgs.Select(FormatArg).ToArray();
        return Loc.F(result.MessageKey, args);
    }

    private static object FormatArg(string arg)
    {
        if (Enum.TryParse<Core.Windows.FirewallProfile>(arg, out var profile) && Enum.IsDefined(profile))
        {
            return Loc.T($"Firewall_{profile}");
        }

        return long.TryParse(arg, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1024
            ? Loc.Bytes(n)
            : arg;
    }

    public void Close() => IsOpen = false;
}
