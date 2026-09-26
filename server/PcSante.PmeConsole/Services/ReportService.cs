using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PcSante.PmeConsole.Data;

namespace PcSante.PmeConsole.Services;

public sealed record DeviceMonthSummary(Guid DeviceId, string MachineName, int? LastScore, double? AverageScore, int Reports, int OpenAlerts, DateTimeOffset? LastReportAt);

/// <summary>Rapport consolidé multi-postes d'un mois (M8, offre PME) : envoyé au gérant et exportable en CSV.</summary>
public sealed record MonthlyReport(string OrganizationName, int Year, int Month, int Devices, double? AverageScore, int OpenAlerts, IReadOnlyList<DeviceMonthSummary> Rows);

public sealed class ReportService(ConsoleDbContext db)
{
    public async Task<MonthlyReport?> BuildAsync(Guid organizationId, int year, int month, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 2020 or > 2100)
        {
            return null;
        }

        var organization = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, ct).ConfigureAwait(false);
        if (organization is null)
        {
            return null;
        }

        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);
        var devices = await db.Devices.AsNoTracking().Where(d => d.OrganizationId == organizationId && !d.Revoked).ToListAsync(ct).ConfigureAwait(false);
        var ids = devices.Select(d => d.Id).ToList();
        var history = await db.ScoreHistory.AsNoTracking().Where(h => ids.Contains(h.DeviceId)).ToListAsync(ct).ConfigureAwait(false);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false);

        var rows = devices.Select(d =>
        {
            var month = history.Where(h => h.DeviceId == d.Id && h.At >= from && h.At < to).ToList();
            return new DeviceMonthSummary(d.Id, d.MachineName, d.Score, month.Count == 0 ? null : Math.Round(month.Average(h => h.Score), 1),
                month.Count, alerts.Count(a => a.DeviceId == d.Id), d.LastReportAt);
        }).OrderBy(r => r.AverageScore ?? 101).ThenBy(r => r.MachineName, StringComparer.CurrentCultureIgnoreCase).ToList();

        var averages = rows.Where(r => r.AverageScore is not null).Select(r => r.AverageScore!.Value).ToList();
        return new MonthlyReport(organization.Name, year, month, rows.Count, averages.Count == 0 ? null : Math.Round(averages.Average(), 1), alerts.Count, rows);
    }

    /// <summary>CSV « ; » UTF-8 avec BOM, cellules protégées contre l'injection de formules.</summary>
    public static byte[] ToCsv(MonthlyReport report, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(report);
        var csv = new StringBuilder();
        Line(csv, [T("Csv_Item", culture), T("Csv_Score", culture), T("Pme_AverageScore", culture), T("Pme_Reports", culture), T("Pme_OpenAlerts", culture), T("Pme_LastReport", culture)]);
        foreach (var r in report.Rows)
        {
            Line(csv, [r.MachineName, r.LastScore?.ToString(culture) ?? string.Empty, r.AverageScore?.ToString(culture) ?? string.Empty,
                r.Reports.ToString(culture), r.OpenAlerts.ToString(culture), r.LastReportAt?.ToString("g", culture) ?? string.Empty]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    internal static string Cell(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string T(string key, CultureInfo culture) => ConsoleStrings.Text(key, culture);

    private static void Line(StringBuilder csv, string[] cells) => csv.Append(string.Join(';', cells.Select(Cell))).Append("\r\n");
}
