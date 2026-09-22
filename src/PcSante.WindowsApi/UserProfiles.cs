using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PcSante.WindowsApi;

public sealed record UserProfile(string Sid, string Path);

/// <summary>Profils des utilisateurs réels du PC (SID S-1-5-21-…), lus depuis le registre.</summary>
[SupportedOSPlatform("windows")]
public static class UserProfiles
{
    public static IReadOnlyList<UserProfile> All()
    {
        using var list = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (list is null)
        {
            return [];
        }

        var profiles = new List<UserProfile>();
        foreach (var sid in list.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !s.EndsWith(".bak", StringComparison.Ordinal)))
        {
            using var key = list.OpenSubKey(sid);
            if (key?.GetValue("ProfileImagePath") is string path)
            {
                path = Environment.ExpandEnvironmentVariables(path);
                if (Directory.Exists(path))
                {
                    profiles.Add(new UserProfile(sid, path));
                }
            }
        }

        return profiles;
    }

    public static string? PathOf(string? sid) =>
        sid is null ? null : All().FirstOrDefault(p => string.Equals(p.Sid, sid, StringComparison.OrdinalIgnoreCase))?.Path;
}
