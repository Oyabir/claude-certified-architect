using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>
/// Nettoyage prudent : dossiers connus uniquement ; fichiers temporaires de plus de 24 h ; aucun lien ni jonction
/// suivi ; suppression sécurisée par handle ; fichiers verrouillés ignorés ; navigateurs ouverts ignorés.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCleanupApi : ICleanupApi
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan EstimateBudget = TimeSpan.FromSeconds(15);

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Device,
        ReturnSpecialDirectories = false,
    };

    private sealed record Root(string Path, bool OldFilesOnly, string? Browser = null);

    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public Task<CleanupEstimate> EstimateAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var watch = Stopwatch.StartNew();
        long Size(IEnumerable<Root> roots) => roots.Sum(r => Enumerate(r).TakeWhile(_ => watch.Elapsed < EstimateBudget).Sum(f => f.Length));
        return new CleanupEstimate(
            Size(Roots(CleanupTarget.TemporaryFiles)),
            Size(Roots(CleanupTarget.WindowsUpdateCache)),
            Size(Roots(CleanupTarget.RecycleBin)),
            Size(Roots(CleanupTarget.BrowserCaches)));
    }, cancellationToken);

    public Task<CleanupResult> CleanAsync(CleanupTarget target, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var stoppedServices = target == CleanupTarget.WindowsUpdateCache ? StopUpdateServices() : [];
        try
        {
            long freed = 0;
            int deleted = 0, skipped = 0;
            foreach (var root in Roots(target))
            {
                if (root.Browser is not null && IsRunning(root.Browser))
                {
                    skipped++;
                    continue;
                }

                foreach (var file in Enumerate(root).ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = file.Length;
                    if (SecureDelete.TryDelete(file.FullName, root.Path, isDirectory: false))
                    {
                        freed += length;
                        deleted++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                RemoveEmptyDirectories(root);
            }

            return new CleanupResult(freed, deleted, skipped);
        }
        finally
        {
            RestartServices(stoppedServices);
        }
    }, cancellationToken);

    private static IEnumerable<Root> Roots(CleanupTarget target)
    {
        var profiles = UserProfiles.All();
        switch (target)
        {
            case CleanupTarget.TemporaryFiles:
                yield return new Root(Path.Combine(Windows, "Temp"), true);
                foreach (var p in profiles)
                {
                    yield return new Root(Path.Combine(p.Path, @"AppData\Local\Temp"), true);
                }

                break;
            case CleanupTarget.WindowsUpdateCache:
                yield return new Root(Path.Combine(Windows, @"SoftwareDistribution\Download"), false);
                break;
            case CleanupTarget.RecycleBin:
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                {
                    var bin = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    if (Directory.Exists(bin))
                    {
                        foreach (var sidFolder in Directory.EnumerateDirectories(bin))
                        {
                            yield return new Root(sidFolder, false);
                        }
                    }
                }

                break;
            case CleanupTarget.BrowserCaches:
                foreach (var p in profiles)
                {
                    foreach (var (browser, pattern) in BrowserCaches)
                    {
                        foreach (var dir in ExpandProfiles(Path.Combine(p.Path, pattern)))
                        {
                            yield return new Root(dir, false, browser);
                        }
                    }
                }

                break;
        }
    }

    private static readonly (string Browser, string Pattern)[] BrowserCaches =
    [
        ("chrome", @"AppData\Local\Google\Chrome\User Data\*\Cache\Cache_Data"),
        ("chrome", @"AppData\Local\Google\Chrome\User Data\*\Code Cache"),
        ("msedge", @"AppData\Local\Microsoft\Edge\User Data\*\Cache\Cache_Data"),
        ("msedge", @"AppData\Local\Microsoft\Edge\User Data\*\Code Cache"),
        ("firefox", @"AppData\Local\Mozilla\Firefox\Profiles\*\cache2"),
    ];

    /// <summary>Remplace le « * » du motif par chaque profil de navigateur existant (sans suivre de lien).</summary>
    private static IEnumerable<string> ExpandProfiles(string pattern)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);
        var parent = pattern[..(star - 1)];
        var rest = pattern[(star + 1)..].TrimStart('\\');
        if (!Directory.Exists(parent))
        {
            return [];
        }

        return new DirectoryInfo(parent).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
            .Select(d => Path.Combine(d.FullName, rest))
            .Where(Directory.Exists)
            .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0);
    }

    private static IEnumerable<FileInfo> Enumerate(Root root)
    {
        if (!Directory.Exists(root.Path) || (File.GetAttributes(root.Path) & FileAttributes.ReparsePoint) != 0)
        {
            return [];
        }

        var limit = DateTime.UtcNow - MinimumAge;
        return new DirectoryInfo(root.Path).EnumerateFiles("*", Walk)
            .Where(f => !root.OldFilesOnly || (f.LastWriteTimeUtc < limit && f.CreationTimeUtc < limit))
            .Where(f => !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));
    }

    private static void RemoveEmptyDirectories(Root root)
    {
        if (!Directory.Exists(root.Path))
        {
            return;
        }

        var limit = DateTime.UtcNow - MinimumAge;
        var directories = new DirectoryInfo(root.Path).EnumerateDirectories("*", Walk)
            .OrderByDescending(d => d.FullName.Length)
            .ToList();
        foreach (var dir in directories)
        {
            try
            {
                if ((!root.OldFilesOnly || dir.LastWriteTimeUtc < limit) && !dir.EnumerateFileSystemInfos().Any())
                {
                    SecureDelete.TryDelete(dir.FullName, root.Path, isDirectory: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier en cours d'utilisation : ignoré.
            }
        }
    }

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes)
        {
            p.Dispose();
        }

        return processes.Length > 0;
    }

    private static List<string> StopUpdateServices()
    {
        var stopped = new List<string>();
        foreach (var name in new[] { "wuauserv", "bits" })
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                    stopped.Add(name);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                // Service indisponible : les fichiers verrouillés seront simplement ignorés.
            }
        }

        return stopped;
    }

    private static void RestartServices(List<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.Start();
            }
            catch (InvalidOperationException)
            {
                // Windows le redémarrera à la demande.
            }
        }
    }
}
