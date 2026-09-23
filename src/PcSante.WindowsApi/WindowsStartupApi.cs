using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>
/// Programmes au démarrage : clés Run (machine, 32 bits, utilisateur) et dossiers Démarrage.
/// L'état activé/désactivé est lu et écrit dans « Explorer\StartupApproved », comme le Gestionnaire des tâches :
/// aucune entrée n'est supprimée, tout reste réversible.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupApi : IStartupApi
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public Task<IReadOnlyList<StartupItem>> ListAsync(string? userSid, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<StartupItem>>(() =>
    {
        var items = new List<StartupItem>();
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        ReadRun(items, hklm, RunKey, ApprovedRoot + @"\Run", StartupLocation.MachineRun);
        ReadRun(items, hklm, Run32Key, ApprovedRoot + @"\Run32", StartupLocation.MachineRun32);
        ReadFolder(items, hklm, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.MachineStartupFolder);

        if (UserHive(userSid) is { } user)
        {
            using (user)
            {
                ReadRun(items, user, RunKey, ApprovedRoot + @"\Run", StartupLocation.UserRun);
                if (UserProfiles.PathOf(userSid) is { } profile)
                {
                    ReadFolder(items, user, Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup"), StartupLocation.UserStartupFolder);
                }
            }
        }

        return items;
    }, cancellationToken);

    public Task<bool> SetEnabledAsync(string id, bool enabled, string? userSid, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var separator = id.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0 || !Enum.TryParse<StartupLocation>(id[..separator], out var location))
        {
            return false;
        }

        var name = id[(separator + 1)..];
        var approvedSubKey = location switch
        {
            StartupLocation.MachineRun or StartupLocation.UserRun => ApprovedRoot + @"\Run",
            StartupLocation.MachineRun32 => ApprovedRoot + @"\Run32",
            _ => ApprovedRoot + @"\StartupFolder",
        };

        RegistryKey? root = location is StartupLocation.UserRun or StartupLocation.UserStartupFolder
            ? UserHive(userSid)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (root is null)
        {
            return false;
        }

        using (root)
        {
            using var key = root.CreateSubKey(approvedSubKey, writable: true);
            key.SetValue(name, ApprovedValue(enabled), RegistryValueKind.Binary);
            return true;
        }
    }, cancellationToken);

    /// <summary>Valeur StartupApproved : octet 0 pair = activé (02), impair = désactivé (03) + date de désactivation.</summary>
    internal static byte[] ApprovedValue(bool enabled)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
        }

        return value;
    }

    internal static bool IsApproved(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    private static void ReadRun(List<StartupItem> items, RegistryKey root, string runKey, string approvedKey, StartupLocation location)
    {
        using var run = root.OpenSubKey(runKey);
        if (run is null)
        {
            return;
        }

        using var approved = root.OpenSubKey(approvedKey);
        foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
        {
            var command = run.GetValue(name)?.ToString() ?? string.Empty;
            var executable = ExtractExecutable(command);
            items.Add(new StartupItem($"{location}|{name}", DisplayName(name, DescriptionOf(executable)), command, location,
                IsApproved(approved?.GetValue(name) as byte[]), executable));
        }
    }

    private static void ReadFolder(List<StartupItem> items, RegistryKey root, string folder, StartupLocation location)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        using var approved = root.OpenSubKey(ApprovedRoot + @"\StartupFolder");
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(file);
            items.Add(new StartupItem($"{location}|{name}", Path.GetFileNameWithoutExtension(file), file, location,
                IsApproved(approved?.GetValue(name) as byte[]), file));
        }
    }

    private static RegistryKey? UserHive(string? sid)
    {
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }

        try
        {
            _ = new SecurityIdentifier(sid);
            return RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(sid, writable: true);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Extrait le chemin de l'exécutable d'une ligne de commande (entre guillemets ou jusqu'à « .exe »).</summary>
    /// <summary>
    /// Nom affiché : la description de l'exécutable (« Microsoft Edge ») plutôt que le nom technique de la valeur
    /// de registre (« MicrosoftEdgeAutoLaunch_507B… »), qui reste l'identifiant.
    /// </summary>
    internal static string DisplayName(string registryName, string? description) =>
        string.IsNullOrWhiteSpace(description) ? registryName : description.Trim();

    private static string? DescriptionOf(string? executable)
    {
        if (string.IsNullOrEmpty(executable))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(Environment.ExpandEnvironmentVariables(executable));
            return string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? ExtractExecutable(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }

        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : null;
    }
}
