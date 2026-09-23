using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Pare-feu Windows : COM INetFwPolicy2 (HNetCfg.FwPolicy2) ; netsh advfirewall pour export/import/réinitialisation.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFirewallApi : IFirewallApi
{
    private static int ProfileBit(FirewallProfile profile) => profile switch
    {
        FirewallProfile.Domain => 1,
        FirewallProfile.Private => 2,
        _ => 4,
    };

    public Task<IReadOnlyList<FirewallProfileStatus>> GetStatusAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<FirewallProfileStatus>>(() =>
    {
        var policy = CreatePolicy();
        try
        {
            return Enum.GetValues<FirewallProfile>()
                .Select(p => new FirewallProfileStatus(p, (bool)policy.GetType().InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, policy, [ProfileBit(p)], System.Globalization.CultureInfo.InvariantCulture)!))
                .ToList();
        }
        finally
        {
            Marshal.FinalReleaseComObject(policy);
        }
    }, cancellationToken);

    public Task<bool> SetProfileEnabledAsync(FirewallProfile profile, bool enabled, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var policy = CreatePolicy();
        try
        {
            policy.GetType().InvokeMember("FirewallEnabled", BindingFlags.SetProperty, null, policy, [ProfileBit(profile), enabled], System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(policy);
        }
    }, cancellationToken);

    public async Task<bool> ExportPolicyAsync(string filePath, CancellationToken cancellationToken) =>
        (await SystemTools.RunAsync(SystemTools.Netsh, ["advfirewall", "export", filePath], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> ImportPolicyAsync(string filePath, CancellationToken cancellationToken) =>
        (await SystemTools.RunAsync(SystemTools.Netsh, ["advfirewall", "import", filePath], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> ResetToDefaultAsync(CancellationToken cancellationToken) =>
        (await SystemTools.RunAsync(SystemTools.Netsh, ["advfirewall", "reset"], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false)).Succeeded;

    private static object CreatePolicy()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }
}
