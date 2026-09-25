using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PcSante.Core;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Ipc;
using PcSante.Ipc.Security;
using PcSante.Licensing;
using PcSante.Service.Actions;
using PcSante.Service.Data;
using PcSante.Service.Diagnostics;
using PcSante.Service.Dispatch;
using PcSante.Service.Pme;
using PcSante.Service.Queries;
using PcSante.Service.Updates;

namespace PcSante.Service;

/// <summary>Enregistrement des composants du service, indépendant de la plateforme (les accès Windows sont injectés à part).</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPcSanteCore(this IServiceCollection services, ServicePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<ServiceDbContext>(o => o.UseSqlite($"Data Source={paths.Database}"));
        services.AddSingleton<IAuditLog, EfAuditLog>();
        services.AddSingleton<IUndoStore, EfUndoStore>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<RestorePointGuard>();
        services.AddSingleton<SystemActionPipeline>();
        services.AddSingleton<BackgroundJobs>();
        services.AddSingleton<HealthAnalyzer>();
        services.AddSingleton<RemoteAccessTracker>();
        services.AddSingleton<QueryRegistry>();
        services.AddSingleton<AppUpdateService>();

        services.AddHttpClient<ILicenseServerClient, HttpLicenseServerClient>(c =>
        {
            if (Uri.TryCreate(ProductInfo.LicenseServerUrl, UriKind.Absolute, out var url))
            {
                c.BaseAddress = url;
            }

            c.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddHttpClient("updates", c => c.Timeout = TimeSpan.FromMinutes(10));
        services.AddHttpClient<IPmeClient, HttpPmeClient>(c =>
        {
            if (Uri.TryCreate(ProductInfo.ConsoleUrl, UriKind.Absolute, out var url))
            {
                c.BaseAddress = url;
            }

            c.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton(sp => new PmeEnrollmentStore(paths.PmeEnrollment, sp.GetRequiredService<ISecretProtector>()));
        services.AddSingleton(sp => new PmeReporter(sp.GetRequiredService<IPmeClient>(), sp.GetRequiredService<PmeEnrollmentStore>(),
            sp.GetRequiredService<ISystemInfoApi>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<PmeReporter>>(), ProductInfo.ConsoleUrl));
        services.AddSingleton<ILicenseStateStore>(sp => new ProtectedFileLicenseStateStore(paths.LicenseState, sp.GetRequiredService<ISecretProtector>()));
        services.AddSingleton(sp => new LicenseManager(
            sp.GetRequiredService<ILicenseServerClient>(),
            sp.GetRequiredService<ILicenseStateStore>(),
            sp.GetRequiredService<IHardwareInfoProvider>(),
            sp.GetRequiredService<TimeProvider>(),
            ProductInfo.LicensePublicKey));

        // Catalogue fermé : requêtes, opérations, actions système.
        services.AddSingleton<IEnumerable<IQueryHandler>>(sp => sp.GetRequiredService<QueryRegistry>().All().ToList());
        services.AddSingleton<IEnumerable<IOperationHandler>>(sp => new List<IOperationHandler>
        {
            new LicenseOperation(CommandId.ActivateLicense, sp.GetRequiredService<LicenseManager>()),
            new LicenseOperation(CommandId.TransferLicense, sp.GetRequiredService<LicenseManager>()),
            new LicenseOperation(CommandId.DeactivateLicense, sp.GetRequiredService<LicenseManager>()),
            new LicenseOperation(CommandId.RevalidateLicense, sp.GetRequiredService<LicenseManager>()),
            new UndoOperation(sp.GetRequiredService<SystemActionPipeline>(), sp),
            new RunTemplateOperation(sp, sp.GetRequiredService<HistoryStore>(), sp.GetRequiredService<TimeProvider>()),
            new InstallUpdateOperation(sp.GetRequiredService<AppUpdateService>()),
            new BitLockerRecoveryKeyOperation(sp.GetRequiredService<IBitLockerApi>(), sp.GetRequiredService<ILocalAccountsApi>()),
            new PmeOperation(CommandId.EnrollInPme, sp.GetRequiredService<PmeReporter>()),
            new PmeOperation(CommandId.LeavePme, sp.GetRequiredService<PmeReporter>()),
            new GenerateMonthlyReportOperation(sp.GetRequiredService<QueryRegistry>(), sp.GetRequiredService<ServicePaths>(), sp.GetRequiredService<TimeProvider>()),
        });
        services.AddSingleton<IEnumerable<SystemAction>>(sp => CreateActions(sp).ToList());
        services.AddSingleton<CommandCatalog>();
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton<IRequestHandler>(sp => sp.GetRequiredService<CommandDispatcher>());
        services.AddSingleton(sp => new PipeServer(
            ProductInfo.PipeName,
            sp.GetRequiredService<IPipeStreamFactory>(),
            sp.GetRequiredService<IClientVerifier>(),
            sp.GetRequiredService<IRequestHandler>(),
            sp.GetRequiredService<ILogger<PipeServer>>()));
        return services;
    }

    private static IEnumerable<SystemAction> CreateActions(IServiceProvider sp)
    {
        T S<T>() where T : notnull => sp.GetRequiredService<T>();

        var defender = S<IDefenderApi>();
        yield return new EnableRealtimeProtectionAction(defender);
        yield return new EnableCloudProtectionAction(defender);
        yield return new EnableControlledFolderAccessAction(defender);
        yield return new ScanAction(CommandId.StartQuickScan, defender, S<BackgroundJobs>());
        yield return new ScanAction(CommandId.StartFullScan, defender, S<BackgroundJobs>());
        yield return new ScanAction(CommandId.StartCustomScan, defender, S<BackgroundJobs>());
        yield return new UpdateSignaturesAction(defender);
        yield return new RestoreQuarantinedItemAction(defender);

        var firewall = S<IFirewallApi>();
        yield return new SetFirewallProfileAction(CommandId.EnableFirewallProfile, firewall);
        yield return new SetFirewallProfileAction(CommandId.DisableFirewallProfile, firewall);
        yield return new ResetFirewallAction(firewall, S<ServicePaths>(), S<TimeProvider>());

        var updates = S<IWindowsUpdateApi>();
        yield return new SearchUpdatesAction(updates);
        yield return new InstallUpdatesAction(updates);
        yield return new RepairWindowsUpdateAction(updates, S<TimeProvider>());

        yield return new RepairAction(CommandId.RunSystemFileCheck, S<ISystemRepairApi>());
        yield return new RepairAction(CommandId.RunDismRepair, S<ISystemRepairApi>());
        yield return new FlushDnsAction(S<INetworkRepairApi>());
        yield return new ResetNetworkStackAction(S<INetworkRepairApi>());
        yield return new DisableGuestAccountAction(S<ILocalAccountsApi>());
        yield return new EnableBitLockerAction(S<IBitLockerApi>(), S<ILocalAccountsApi>());
        yield return new ServiceProfileAction(S<IServiceControlApi>());
        yield return new LightenVisualEffectsAction(S<IVisualEffectsApi>());
        yield return new OptimizeSystemDriveAction(S<IDiskOptimizationApi>(), S<BackgroundJobs>());
        yield return new PageFileAutomaticAction(S<IDiskOptimizationApi>());
        foreach (var command in new[] { CommandId.SendSessionMessage, CommandId.DisconnectSession, CommandId.LogOffSession })
        {
            yield return new SessionAction(command, S<ISessionApi>(), S<ILocalAccountsApi>());
        }
        yield return new CreateRestorePointAction(S<IRestorePointApi>(), S<TimeProvider>());
        yield return new EnableSystemRestoreAction(S<IRestorePointApi>());

        yield return new StopProcessAction(S<IProcessApi>(), S<ISystemInfoApi>());
        yield return new DisableServiceAction(S<IServiceControlApi>());

        yield return new StartupItemAction(CommandId.DisableStartupItem, S<IStartupApi>());
        yield return new StartupItemAction(CommandId.EnableStartupItem, S<IStartupApi>());
        yield return new ThirdPartyTaskAction(CommandId.DisableThirdPartyTask, S<IThirdPartyTaskApi>());
        yield return new ThirdPartyTaskAction(CommandId.EnableThirdPartyTask, S<IThirdPartyTaskApi>());
        yield return new CleanupAction(CommandId.CleanTemporaryFiles, S<ICleanupApi>());
        yield return new CleanupAction(CommandId.CleanWindowsUpdateCache, S<ICleanupApi>());
        yield return new CleanupAction(CommandId.EmptyRecycleBin, S<ICleanupApi>());
        yield return new CleanupAction(CommandId.CleanBrowserCaches, S<ICleanupApi>());
        yield return new SetPowerPlanAction(S<IPowerApi>());

        yield return new ScheduleTemplateAction(CommandId.EnableScheduledTemplate, S<IScheduledTemplateApi>());
        yield return new ScheduleTemplateAction(CommandId.DisableScheduledTemplate, S<IScheduledTemplateApi>());
    }

    /// <summary>Crée la base si nécessaire.</summary>
    public static void EnsureDatabase(IServiceProvider services)
    {
        using var db = services.GetRequiredService<IDbContextFactory<ServiceDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }
}
