using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

/// <summary>Session affichée : utilisateur, locale ou à distance (adresse), état, heure d'ouverture.</summary>
public sealed record SessionRow(int SessionId, string User, string Origin, string State, string Since, SessionState RawState = SessionState.Other)
{
    /// <summary>Initiale du nom d'utilisateur (sans le domaine) pour l'avatar.</summary>
    public string Initial
    {
        get
        {
            var name = User.Contains('\\', StringComparison.Ordinal) ? User[(User.LastIndexOf('\\') + 1)..] : User;
            return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpper(Loc.Culture);
        }
    }

    public Infrastructure.Tone StateTone => RawState switch
    {
        SessionState.Active => Infrastructure.Tone.Good,
        SessionState.Disconnected => Infrastructure.Tone.Warn,
        _ => Infrastructure.Tone.Neutral,
    };
}

/// <summary>Sessions locales et Bureau à distance (M7, mode Avancé) : message, déconnexion, fermeture.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class SessionsViewModel(MainViewModel main) : PageViewModel(main)
{
    public override ScreenId Screen => ScreenId.Sessions;

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    public IReadOnlyList<Choice> Messages { get; } =
        CommandDefinitions.SessionMessages.Select(m => new Choice(m, Loc.T($"SessionMessage_{m}"))).ToList();

    [ObservableProperty]
    private string _selectedMessage = CommandDefinitions.SessionMessages[0];

    public override async Task LoadAsync()
    {
        Sessions.Clear();
        foreach (var s in await Query<List<UserSession>>(CommandId.GetSessions).ConfigureAwait(true) ?? [])
        {
            Sessions.Add(new SessionRow(
                s.SessionId,
                s.UserName,
                s.IsRemote ? (s.ClientAddress is { } ip ? Loc.F("Sessions_Remote", ip) : Loc.T("Sessions_RemoteUnknown")) : Loc.T("Sessions_Local"),
                Loc.T($"SessionState_{s.State}"),
                s.LogonTime is { } at ? Loc.F("Sessions_Since", Loc.When(at)) : string.Empty,
                s.State));
        }
    }

    /// <summary>Bouton principal : actualiser la liste.</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private Task SendMessageAsync(SessionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(CommandId.SendSessionMessage, new Dictionary<string, string>
        {
            ["sessionId"] = row.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["message"] = SelectedMessage,
            ["language"] = Main.Settings.Language,
        });
    }

    [RelayCommand]
    private Task DisconnectAsync(SessionRow row) => RunOnSessionAsync(CommandId.DisconnectSession, row);

    [RelayCommand]
    private Task LogOffAsync(SessionRow row) => RunOnSessionAsync(CommandId.LogOffSession, row);

    private Task RunOnSessionAsync(CommandId command, SessionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecuteAsync(command,
            new Dictionary<string, string> { ["sessionId"] = row.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            confirmSubject: Loc.F("Sessions_ConfirmSubject", Loc.T($"Command_{command}"), row.User));
    }
}
