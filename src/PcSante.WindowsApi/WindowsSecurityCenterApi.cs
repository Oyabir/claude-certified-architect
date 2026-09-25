using System.Management;
using System.Runtime.Versioning;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Antivirus déclarés au Centre de sécurité Windows (root\SecurityCenter2), lecture seule.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityCenterApi : ISecurityCenterApi
{
    public Task<IReadOnlyList<AntivirusProduct>> ListAntivirusAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<AntivirusProduct>>(() =>
    {
        try
        {
            return WmiHelper.Query(WmiHelper.SecurityCenterNamespace, "SELECT displayName, productState, pathToSignedProductExe FROM AntiVirusProduct")
                .Select(p => (Name: WmiHelper.Get<string>(p, "displayName"), State: WmiHelper.Get<uint>(p, "productState"),
                    Path: WmiHelper.Get<string>(p, "pathToSignedProductExe")))
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new AntivirusProduct(p.Name!, WindowsDefenderApi.IsProductEnabled(p.State), IsSignatureUpToDate(p.State),
                    WindowsDefenderApi.IsDefenderProduct(p.Path)))
                .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }, cancellationToken);

    /// <summary>productState : les bits 4 à 7 valent 0 quand les signatures sont à jour (ex. 0x061100), 1 sinon (0x061110).</summary>
    internal static bool IsSignatureUpToDate(uint productState) => ((productState >> 4) & 0xF) == 0;
}
