using PcSante.Core.Audit;
using PcSante.Core.Health;
using PcSante.Core.Windows;

namespace PcSante.Core.Reporting;

public enum ReportPeriod
{
    Week,
    Month,
}

/// <summary>Données du rapport PDF (M8), fournies par le service.</summary>
public sealed record ReportData(
    ReportPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To,
    string MachineName,
    HealthReport? CurrentHealth,
    IReadOnlyList<ScorePoint> ScoreHistory,
    IReadOnlyList<ThreatInfo> Threats,
    IReadOnlyList<AuditEntry> Actions);
