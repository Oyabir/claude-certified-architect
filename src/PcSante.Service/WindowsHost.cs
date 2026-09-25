using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Ipc.Security;
using PcSante.Ipc.Windows;
using PcSante.Licensing;
using PcSante.Service.Updates;
using PcSante.WindowsApi;

namespace PcSante.Service;

/// <summary>Branchement des implémentations Windows réelles (uniquement sur Windows).</summary>
[SupportedOSPlatform("windows")]
public static class WindowsHost
{
    public static IServiceCollection AddWindowsImplementations(this IServiceCollection services, ServicePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        services.AddSingleton<IDefenderApi, WindowsDefenderApi>();
        services.AddSingleton<IFirewallApi, WindowsFirewallApi>();
        services.AddSingleton<IWindowsUpdateApi, WindowsUpdateApi>();
        services.AddSingleton<ISystemRepairApi, WindowsSystemRepairApi>();
        services.AddSingleton<INetworkRepairApi, WindowsNetworkRepairApi>();
        services.AddSingleton<ILocalAccountsApi, WindowsLocalAccountsApi>();
        services.AddSingleton<ISecurityCenterApi, WindowsSecurityCenterApi>();
        services.AddSingleton<IBitLockerApi, WindowsBitLockerApi>();
        services.AddSingleton<IRestorePointApi, WindowsRestorePointApi>();
        services.AddSingleton<IProcessApi, WindowsProcessApi>();
        services.AddSingleton<IServiceControlApi, WindowsServiceControlApi>();
        services.AddSingleton<ISignatureVerifier, WindowsSignatureVerifier>();
        services.AddSingleton<IStartupApi, WindowsStartupApi>();
        services.AddSingleton<IThirdPartyTaskApi, WindowsThirdPartyTaskApi>();
        services.AddSingleton<ICleanupApi, WindowsCleanupApi>();
        services.AddSingleton<IPowerApi, WindowsPowerApi>();
        services.AddSingleton<IMetricsProvider, WindowsMetricsProvider>();
        services.AddSingleton<ISystemInfoApi, WindowsSystemInfoApi>();
        services.AddSingleton<IScheduledTemplateApi>(_ => new WindowsScheduledTemplateApi(paths.ServiceExecutable));
        services.AddSingleton<IHardwareInfoProvider, WindowsHardwareInfoProvider>();
        services.AddSingleton<ISecretProtector, DpapiMachineProtector>();
        services.AddSingleton<IInstallerLauncher, MsiInstallerLauncher>();

        // Named pipe : ACL + vérification du client (dossier d'installation + même signataire en Release).
        services.AddSingleton<IPipeStreamFactory, SecurePipeStreamFactory>();
        services.AddSingleton<IClientVerifier>(sp =>
        {
            var system = sp.GetRequiredService<ISystemInfoApi>();
            return new ClientVerifier(
                new WindowsClientProcessInspector(system.GetProcessOwner),
                sp.GetRequiredService<ISignatureVerifier>(),
                new ClientTrustPolicy(paths.InstallDirectory, ClientTrustPolicy.ApplicationExecutables, RequireSignature),
                paths.ServiceExecutable);
        });
        return services;
    }

    /// <summary>Toujours vrai en Release : seuls les exécutables signés par le même certificat sont acceptés.</summary>
    public static bool RequireSignature =>
#if DEBUG
        Environment.GetEnvironmentVariable("PCSANTE_DEV_UNSIGNED") != "1";
#else
        true;
#endif

    /// <summary>Dossier de données réservé à SYSTEM et aux administrateurs (journal, jeton, sauvegardes).</summary>
    public static void ProtectDataFolder(ServicePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(paths.DataRoot).SetAccessControl(security);
    }
}

[SupportedOSPlatform("windows")]
public sealed class MsiInstallerLauncher : IInstallerLauncher
{
    public bool Launch(string msiPath)
    {
        var start = new ProcessStartInfo(SystemTools.MsiExec) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/i", msiPath, "/qn", "/norestart", "/l*v", msiPath + ".log" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start);
        return process is not null;
    }
}
