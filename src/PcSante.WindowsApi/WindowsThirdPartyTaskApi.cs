using System.Runtime.Versioning;
using Microsoft.Win32.TaskScheduler;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Tâches planifiées d'éditeurs tiers (hors \Microsoft\ et hors \PcSante\).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsThirdPartyTaskApi : IThirdPartyTaskApi
{
    public System.Threading.Tasks.Task<IReadOnlyList<ThirdPartyTask>> ListAsync(CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run<IReadOnlyList<ThirdPartyTask>>(() =>
        {
            using var service = new TaskService();
            return service.AllTasks
                .Where(t => IsThirdParty(t.Path))
                .Select(t => new ThirdPartyTask(t.Path, t.Name, t.Definition.RegistrationInfo.Author, t.Enabled))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);

    public System.Threading.Tasks.Task<bool> SetEnabledAsync(string path, bool enabled, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run(() =>
        {
            if (!IsThirdParty(path))
            {
                return false;
            }

            using var service = new TaskService();
            var task = service.GetTask(path);
            if (task is null)
            {
                return false;
            }

            task.Enabled = enabled;
            return true;
        }, cancellationToken);

    internal static bool IsThirdParty(string path) =>
        !path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith(@"\" + Core.ProductInfo.TechnicalName + @"\", StringComparison.OrdinalIgnoreCase);
}
