using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Effets visuels dans le registre du profil de l'utilisateur appelant (HKEY_USERS\SID), jamais celui de SYSTEM.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVisualEffectsApi : IVisualEffectsApi
{
    private const string Desktop = @"Control Panel\Desktop";
    private const string WindowMetrics = @"Control Panel\Desktop\WindowMetrics";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VisualEffects = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    public Task<VisualEffectsSettings?> ReadAsync(string userSid, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var hive = OpenHive(userSid, writable: false);
        if (hive is null)
        {
            return null;
        }

        return (VisualEffectsSettings?)new VisualEffectsSettings(
            Get(hive, Desktop, "UserPreferencesMask") as byte[],
            Get(hive, WindowMetrics, "MinAnimate") as string,
            Get(hive, Advanced, "TaskbarAnimations") as int?,
            Get(hive, VisualEffects, "VisualFXSetting") as int?);
    }, cancellationToken);

    public Task<bool> WriteAsync(string userSid, VisualEffectsSettings settings, CancellationToken cancellationToken) => Task.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var hive = OpenHive(userSid, writable: true);
        if (hive is null)
        {
            return false;
        }

        Set(hive, Desktop, "UserPreferencesMask", settings.UserPreferencesMask, RegistryValueKind.Binary);
        Set(hive, WindowMetrics, "MinAnimate", settings.MinAnimate, RegistryValueKind.String);
        Set(hive, Advanced, "TaskbarAnimations", settings.TaskbarAnimations, RegistryValueKind.DWord);
        Set(hive, VisualEffects, "VisualFXSetting", settings.VisualFxSetting, RegistryValueKind.DWord);
        return true;
    }, cancellationToken);

    private static RegistryKey? OpenHive(string userSid, bool writable)
    {
        // SID validé : il vient de l'identité du client du named pipe, jamais d'une saisie.
        var sid = new SecurityIdentifier(userSid).Value;
        return RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(sid, writable);
    }

    private static object? Get(RegistryKey hive, string path, string name)
    {
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name);
    }

    /// <summary>Écrit la valeur, ou la supprime si null (retour au réglage par défaut de Windows).</summary>
    private static void Set(RegistryKey hive, string path, string name, object? value, RegistryValueKind kind)
    {
        using var key = hive.CreateSubKey(path, writable: true);
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, kind);
        }
    }
}
