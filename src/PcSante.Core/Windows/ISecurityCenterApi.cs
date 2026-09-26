namespace PcSante.Core.Windows;

/// <summary>Antivirus déclaré au Centre de sécurité Windows (M2) : Defender ou antivirus tiers.</summary>
public sealed record AntivirusProduct(string Name, bool Enabled, bool UpToDate, bool IsDefender);

/// <summary>Centre de sécurité Windows (consultation seulement : PC Santé ne pilote pas les antivirus tiers).</summary>
public interface ISecurityCenterApi
{
    Task<IReadOnlyList<AntivirusProduct>> ListAntivirusAsync(CancellationToken cancellationToken);
}
