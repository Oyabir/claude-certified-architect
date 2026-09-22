using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Windows Update Agent (COM « Microsoft.Update.Session ») et services wuauserv/BITS.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateApi : IWindowsUpdateApi
{
    private const string Criteria = "IsInstalled=0 and IsHidden=0 and Type='Software'";
    private static readonly string[] UpdateServices = ["wuauserv", "bits"];

    private static string SoftwareDistribution =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution");

    public Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        dynamic auto = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate", true)!)!;
        dynamic info = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo", true)!)!;
        try
        {
            dynamic results = auto.Results;
            DateTimeOffset? lastSearch = ToDate(results.LastSearchSuccessDate);
            DateTimeOffset? lastInstall = ToDate(results.LastInstallationSuccessDate);
            bool reboot = info.RebootRequired;
            return new UpdateStatus(lastSearch, lastInstall, reboot, ServiceUsable("wuauserv"));
        }
        finally
        {
            Marshal.FinalReleaseComObject(auto);
            Marshal.FinalReleaseComObject(info);
        }
    }, cancellationToken);

    public Task<IReadOnlyList<PendingUpdate>> SearchAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<PendingUpdate>>(() =>
    {
        dynamic session = CreateSession();
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search(Criteria);
        var list = new List<PendingUpdate>();
        foreach (dynamic update in result.Updates)
        {
            string id = update.Identity.UpdateID;
            string title = update.Title;
            string? severity = update.MsrcSeverity;
            bool important = update.AutoSelectOnWebSites || !string.IsNullOrEmpty(severity);
            long size = Convert.ToInt64(update.MaxDownloadSize, CultureInfo.InvariantCulture);
            list.Add(new PendingUpdate(id, title, important, size));
        }

        return list;
    }, cancellationToken);

    public Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        dynamic session = CreateSession();
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search(Criteria);
        dynamic toInstall = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true)!)!;
        foreach (dynamic update in found.Updates)
        {
            // Installation demandée explicitement par l'utilisateur : même comportement que l'application Paramètres.
            if (!update.EulaAccepted)
            {
                update.AcceptEula();
            }

            toInstall.Add(update);
        }

        if (toInstall.Count == 0)
        {
            return new UpdateInstallResult(0, 0, false);
        }

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = toInstall;
        downloader.Download();

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = toInstall;
        dynamic result = installer.Install();
        int installed = 0, failed = 0;
        for (var i = 0; i < toInstall.Count; i++)
        {
            int code = result.GetUpdateResult(i).ResultCode;
            if (code is 2 or 3)
            {
                installed++;
            }
            else
            {
                failed++;
            }
        }

        return new UpdateInstallResult(installed, failed, (bool)result.RebootRequired);
    }, cancellationToken);

    public string ProposeBackupPath(DateTimeOffset now) =>
        SoftwareDistribution + string.Create(CultureInfo.InvariantCulture, $".pcsante-{now:yyyyMMdd-HHmmss}");

    public async Task<bool> ResetComponentsAsync(string backupPath, CancellationToken cancellationToken)
    {
        await StopServicesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(SoftwareDistribution))
            {
                Directory.Move(SoftwareDistribution, backupPath);
            }
        }
        catch (IOException)
        {
            await StartServicesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await StartServicesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RestoreComponentsAsync(string backupPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(backupPath))
        {
            return false;
        }

        await StopServicesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(SoftwareDistribution))
            {
                Directory.Move(SoftwareDistribution, SoftwareDistribution + ".pcsante-annule-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            }

            Directory.Move(backupPath, SoftwareDistribution);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            await StartServicesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<bool> AreServicesHealthyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(UpdateServices.All(ServiceUsable) && Directory.Exists(SoftwareDistribution));

    private static bool ServiceUsable(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            return sc.StartType != System.ServiceProcess.ServiceStartMode.Disabled;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Task StopServicesAsync(CancellationToken ct) => Task.Run(() =>
    {
        foreach (var name in UpdateServices)
        {
            using var sc = new ServiceController(name);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
            }
        }
    }, ct);

    private static Task StartServicesAsync(CancellationToken ct) => Task.Run(() =>
    {
        foreach (var name in UpdateServices)
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped && sc.StartType != System.ServiceProcess.ServiceStartMode.Disabled)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
            }
        }
    }, ct);

    private static dynamic CreateSession()
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!)!;
        session.ClientApplicationID = Core.ProductInfo.Name;
        return session;
    }

    private static DateTimeOffset? ToDate(object? value) => value switch
    {
        DateTime dt when dt.Year > 1990 => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => null,
    };
}
