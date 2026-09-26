using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PcSante.PmeConsole.Data;
using PcSante.PmeConsole.Endpoints;

namespace PcSante.PmeConsole.Services;

/// <summary>
/// Tâches périodiques de la console : postes silencieux, e-mail des nouvelles alertes aux gérants (un e-mail par
/// organisation, pas un par alerte), rapport consolidé du mois écoulé le 1er du mois (avec le CSV en pièce jointe).
/// </summary>
public sealed class ConsoleJobs(ConsoleDbContext db, DeviceService devices, ReportService reports, IMailSender mailer, TimeProvider time)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    public async Task RunAsync(CancellationToken ct)
    {
        await devices.CheckSilentDevicesAsync(ct).ConfigureAwait(false);
        await NotifyAlertsAsync(ct).ConfigureAwait(false);
        await SendMonthlyReportsAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> NotifyAlertsAsync(CancellationToken ct)
    {
        var pending = await db.Alerts.Where(a => a.NotifiedAt == null && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false);
        var sent = 0;
        foreach (var group in pending.GroupBy(a => a.OrganizationId))
        {
            var owners = await OwnersAsync(group.Key, ct).ConfigureAwait(false);
            var names = await db.Devices.AsNoTracking().Where(d => d.OrganizationId == group.Key).ToDictionaryAsync(d => d.Id, d => d.MachineName, ct).ConfigureAwait(false);
            var rows = new StringBuilder();
            foreach (var alert in group.OrderBy(a => a.CreatedAt))
            {
                rows.Append(CultureInfo.InvariantCulture, $"<li><strong>{Html(names.GetValueOrDefault(alert.DeviceId, "?"))}</strong> : {Html(ManagerApi.AlertText(alert, French))}</li>");
            }

            var subject = string.Format(French, "PC Santé : {0} nouvelle(s) alerte(s)", group.Count());
            var body = Page(subject, $"<ul>{rows}</ul><p>Détails dans la console d'entreprise, onglet « Alertes ».</p>");
            if (owners.Count > 0 && await mailer.SendAsync(new OutgoingMail(owners, subject, body), ct).ConfigureAwait(false))
            {
                foreach (var alert in group)
                {
                    alert.NotifiedAt = time.GetUtcNow();
                }

                sent++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sent;
    }

    /// <summary>Le 1er du mois (ou dès que possible ensuite), une seule fois par organisation et par mois.</summary>
    public async Task<int> SendMonthlyReportsAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var currentMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var previous = currentMonth.AddMonths(-1);
        var organizations = await db.Organizations.Where(o => o.LastMonthlyReportAt == null || o.LastMonthlyReportAt < currentMonth).ToListAsync(ct).ConfigureAwait(false);
        var sent = 0;
        foreach (var organization in organizations.Where(o => o.CreatedAt < currentMonth))
        {
            if (await reports.BuildAsync(organization.Id, previous.Year, previous.Month, ct).ConfigureAwait(false) is not { } report)
            {
                continue;
            }

            var owners = await OwnersAsync(organization.Id, ct).ConfigureAwait(false);
            var month = previous.ToString("MMMM yyyy", French);
            var rows = new StringBuilder();
            foreach (var r in report.Rows)
            {
                rows.Append(CultureInfo.InvariantCulture,
                    $"<tr><td>{Html(r.MachineName)}</td><td>{r.LastScore?.ToString(French) ?? "–"}</td><td>{r.AverageScore?.ToString(French) ?? "–"}</td><td>{r.OpenAlerts}</td></tr>");
            }

            var subject = $"PC Santé : rapport de {month} — {organization.Name}";
            var body = Page(subject,
                $"<p>{report.Devices} poste(s), score moyen du mois : <strong>{report.AverageScore?.ToString(French) ?? "–"}</strong>, alertes ouvertes : {report.OpenAlerts}.</p>"
                + "<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\"><tr><th>Poste</th><th>Dernier score</th><th>Score moyen</th><th>Alertes</th></tr>"
                + rows + "</table><p>Le détail est joint au format CSV (tableur).</p>");
            var csv = ReportService.ToCsv(report, French);
            if (owners.Count > 0 && await mailer.SendAsync(new OutgoingMail(owners, subject, body, $"pcsante-pme-{previous:yyyy-MM}.csv", csv), ct).ConfigureAwait(false))
            {
                organization.LastMonthlyReportAt = now;
                sent++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sent;
    }

    private async Task<List<string>> OwnersAsync(Guid organizationId, CancellationToken ct) =>
        await db.Managers.AsNoTracking().Where(m => m.OrganizationId == organizationId && m.Role == ManagerRole.Owner)
            .Select(m => m.Email).ToListAsync(ct).ConfigureAwait(false);

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string Page(string title, string content) =>
        $"<!doctype html><html lang=\"fr\"><body style=\"font-family:Segoe UI,Arial,sans-serif\"><h2>{Html(title)}</h2>{content}"
        + "<p style=\"color:#57606a;font-size:12px\">Données techniques uniquement (score, problèmes). E-mail envoyé automatiquement par la console PC Santé.</p></body></html>";
}

/// <summary>Exécution des tâches de la console toutes les 5 minutes.</summary>
public sealed partial class ConsoleJobsWorker(IServiceScopeFactory scopes, ILogger<ConsoleJobsWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ILogger<ConsoleJobsWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ConsoleJobs>().RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Console PME : tâches périodiques en échec")]
    private partial void LogFailed(Exception ex);
}
