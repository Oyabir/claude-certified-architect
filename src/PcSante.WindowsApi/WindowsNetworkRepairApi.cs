using System.Runtime.Versioning;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>ipconfig et netsh de System32, arguments fixes (liste fermée, aucune saisie utilisateur).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkRepairApi : INetworkRepairApi
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public async Task<bool> FlushDnsAsync(CancellationToken cancellationToken) =>
        (await SystemTools.RunAsync(SystemTools.Ipconfig, ["/flushdns"], Timeout, cancellationToken).ConfigureAwait(false)).Succeeded;

    public async Task<bool> ResetNetworkStackAsync(CancellationToken cancellationToken)
    {
        var winsock = await SystemTools.RunAsync(SystemTools.Netsh, ["winsock", "reset"], Timeout, cancellationToken).ConfigureAwait(false);
        var ip = await SystemTools.RunAsync(SystemTools.Netsh, ["int", "ip", "reset"], Timeout, cancellationToken).ConfigureAwait(false);
        return winsock.Succeeded && ip.Succeeded;
    }
}
