using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Scheduling;
using PcSante.Ipc;
using PcSante.Ipc.Windows;
using PcSante.Service;
using PcSante.Service.Workers;
using Serilog;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Ce service ne fonctionne que sous Windows.");
    return 2;
}

var paths = ServicePaths.Default();

// Mode tâche planifiée : « PcSante.Service.exe --run-task WeeklyCleanup » (lancé par le Planificateur en SYSTEM).
// Option « --lang fr|en|ar » : langue des documents produits (rapport mensuel), validée ensuite par le catalogue.
if ((args.Length == 2 || (args.Length == 4 && args[2] == "--lang")) && args[0] == "--run-task")
{
    if (!Enum.TryParse<ScheduledTemplateId>(args[1], ignoreCase: false, out var template) || !Enum.IsDefined(template))
    {
        return 3;
    }

    var parameters = new Dictionary<string, string> { ["template"] = template.ToString() };
    if (args.Length == 4)
    {
        parameters["language"] = args[3];
    }

    await using var client = new PipeClient(ProductInfo.PipeName, new WindowsServerVerifier(paths.ServiceExecutable), TimeSpan.FromSeconds(30));
    var result = await client.SendAsync(CommandId.RunScheduledTemplate, parameters).ConfigureAwait(false);
    return result.IsSuccess ? 0 : 1;
}

// Désinstallation (action personnalisée de l'installeur, en SYSTEM) : retire les tâches planifiées de PC Santé.
if (args.Length == 1 && args[0] == "--uninstall-cleanup")
{
    var scheduler = new PcSante.WindowsApi.WindowsScheduledTemplateApi(paths.ServiceExecutable);
    foreach (var template in Enum.GetValues<ScheduledTemplateId>())
    {
        await scheduler.UnregisterAsync(template, CancellationToken.None).ConfigureAwait(false);
    }

    PcSante.WindowsApi.WindowsScheduledTemplateApi.DeleteFolder();
    return 0;
}

paths.EnsureCreated();
WindowsHost.ProtectDataFolder(paths);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(paths.Logs, "service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30,
        fileSizeLimitBytes: 10 * 1024 * 1024, formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(o => o.ServiceName = ProductInfo.ServiceName);
    builder.Services.AddSerilog();
    builder.Services.AddPcSanteCore(paths);
    builder.Services.AddWindowsImplementations(paths);
    builder.Services.AddHostedService<PipeServerWorker>();
    builder.Services.AddHostedService<ProcessHistoryWorker>();
    builder.Services.AddHostedService<LicenseWorker>();

    using var host = builder.Build();
    ServiceRegistration.EnsureDatabase(host.Services);
    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Arrêt inattendu du service");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
