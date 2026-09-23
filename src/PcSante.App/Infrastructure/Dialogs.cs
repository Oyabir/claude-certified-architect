using System.Runtime.Versioning;
using Microsoft.Win32;
using PcSante.App.Localization;
using PcSante.Core.Commands;

namespace PcSante.App.Infrastructure;

/// <summary>Boîtes de dialogue (confirmation obligatoire avant fermeture de programme, redémarrage, suppression).</summary>
[SupportedOSPlatform("windows")]
public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                FontSize = 15,
                MaxWidth = 460,
            },
            PrimaryButtonText = confirmText,
            CloseButtonText = Loc.T("Common_Cancel"),
            FlowDirection = Loc.IsRightToLeft ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
        };
        var result = await box.ShowDialogAsync().ConfigureAwait(true);
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    /// <summary>Texte de confirmation adapté au type d'action (section 11).</summary>
    public static Task<bool> ConfirmCommandAsync(CommandId command, string? subject = null)
    {
        var kind = CommandDefinitions.Get(command).Confirmation;
        var message = Loc.F($"Confirm_{kind}", subject ?? Loc.T($"Command_{command}"));
        return ConfirmAsync(Loc.T($"Command_{command}"), message, Loc.T("Common_Continue"));
    }

    public static string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Protection_PickFolder") };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static string? SaveFile(string defaultName, string filterKey, string extension)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultName,
            DefaultExt = extension,
            Filter = $"{Loc.T(filterKey)}|*{extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
