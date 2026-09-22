using System.Runtime.Versioning;
using Microsoft.Win32.TaskScheduler;
using PcSante.Core.Scheduling;

namespace PcSante.WindowsApi;

/// <summary>
/// Modèles de tâches dans le Planificateur Windows (dossier \PcSante\), exécutés en SYSTEM par
/// « PcSante.Service.exe --run-task &lt;modèle&gt; », qui repasse par le named pipe et le catalogue.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScheduledTemplateApi(string serviceExecutable) : IScheduledTemplateApi
{
    private static string Folder => @"\" + Core.ProductInfo.TechnicalName;

    private static string TaskPath(ScheduledTemplateId id) => $@"{Folder}\{id}";

    public System.Threading.Tasks.Task<bool> RegisterAsync(ScheduledTemplateId template, ScheduleSettings settings, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(settings);
            using var service = new TaskService();
            var folder = service.RootFolder.SubFolders.Exists(Core.ProductInfo.TechnicalName)
                ? service.GetFolder(Folder)
                : service.RootFolder.CreateFolder(Core.ProductInfo.TechnicalName);

            var definition = service.NewTask();
            definition.RegistrationInfo.Author = Core.ProductInfo.Name;
            definition.RegistrationInfo.Description = $"{Core.ProductInfo.Name} : {template}";
            definition.Principal.UserId = "SYSTEM";
            definition.Principal.LogonType = TaskLogonType.ServiceAccount;
            definition.Principal.RunLevel = TaskRunLevel.Highest;

            var start = DateTime.Today.Add(settings.Time.ToTimeSpan());
            Trigger trigger = settings.Day is { } day
                ? new WeeklyTrigger(ToDaysOfWeek(day)) { StartBoundary = start }
                : new DailyTrigger { StartBoundary = start };
            definition.Triggers.Add(trigger);
            definition.Actions.Add(new ExecAction(serviceExecutable, $"--run-task {template}", Path.GetDirectoryName(serviceExecutable)));

            definition.Settings.StartWhenAvailable = true;
            definition.Settings.ExecutionTimeLimit = TimeSpan.FromHours(4);
            definition.Settings.DisallowStartIfOnBatteries = settings.OnlyOnAcPower;
            definition.Settings.StopIfGoingOnBatteries = settings.OnlyOnAcPower;
            definition.Settings.RunOnlyIfIdle = settings.OnlyWhenIdle;
            if (settings.OnlyWhenIdle)
            {
                definition.Settings.IdleSettings.IdleDuration = TimeSpan.FromMinutes(10);
                definition.Settings.IdleSettings.WaitTimeout = TimeSpan.FromHours(2);
                definition.Settings.IdleSettings.StopOnIdleEnd = false;
            }

            folder.RegisterTaskDefinition(template.ToString(), definition, TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);
            return true;
        }, cancellationToken);

    public System.Threading.Tasks.Task<bool> UnregisterAsync(ScheduledTemplateId template, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run(() =>
        {
            using var service = new TaskService();
            if (service.GetTask(TaskPath(template)) is null)
            {
                return true;
            }

            service.GetFolder(Folder).DeleteTask(template.ToString(), exceptionOnNotExists: false);
            return true;
        }, cancellationToken);

    public System.Threading.Tasks.Task<ScheduleSettings?> GetAsync(ScheduledTemplateId template, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run(() =>
        {
            using var service = new TaskService();
            using var task = service.GetTask(TaskPath(template));
            if (task is null || !task.Enabled)
            {
                return (ScheduleSettings?)null;
            }

            var trigger = task.Definition.Triggers.FirstOrDefault();
            DayOfWeek? day = trigger is WeeklyTrigger weekly ? FromDaysOfWeek(weekly.DaysOfWeek) : null;
            var time = TimeOnly.FromDateTime(trigger?.StartBoundary ?? DateTime.Today);
            return new ScheduleSettings(day, new TimeOnly(time.Hour, time.Minute), task.Definition.Settings.RunOnlyIfIdle, task.Definition.Settings.DisallowStartIfOnBatteries);
        }, cancellationToken);

    internal static DaysOfTheWeek ToDaysOfWeek(DayOfWeek day) => (DaysOfTheWeek)(1 << (int)day);

    internal static DayOfWeek FromDaysOfWeek(DaysOfTheWeek days)
    {
        for (var i = 0; i < 7; i++)
        {
            if (((int)days & (1 << i)) != 0)
            {
                return (DayOfWeek)i;
            }
        }

        return DayOfWeek.Sunday;
    }
}
