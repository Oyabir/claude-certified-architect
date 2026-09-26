using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PcSante.Core;
using PcSante.Core.Health;
using PcSante.Core.Pme;
using PcSante.PmeConsole.Data;

namespace PcSante.PmeConsole.Services;

/// <summary>
/// Inscription des postes et réception de leurs rapports. Les alertes sont tenues à jour à chaque rapport :
/// score rouge, problème critique ; et par vérification périodique : poste silencieux.
/// </summary>
public sealed partial class DeviceService(ConsoleDbContext db, TimeProvider time, ILogger<DeviceService> logger)
{
    public const string ScoreRed = "ScoreRed";
    public const string CriticalIssue = "CriticalIssue";
    public const string Silent = "Silent";

    /// <summary>Un poste sans rapport depuis ce délai est signalé (éteint, désinstallé ou hors réseau).</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromDays(3);

    private readonly ILogger<DeviceService> _logger = logger;

    public async Task<EnrollResponse> EnrollAsync(EnrollRequest? request, CancellationToken ct)
    {
        var code = EnrollmentCodeFormat.Normalize(request?.EnrollmentCode);
        if (request is null || code is null || string.IsNullOrWhiteSpace(request.MachineName))
        {
            return EnrollResponse.Fail(PmeError.InvalidRequest);
        }

        var hash = EnrollmentCodeFormat.Hash(code);
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.EnrollmentCodeHash == hash, ct).ConfigureAwait(false);
        if (organization is null)
        {
            LogRefused("enroll", "InvalidCode");
            return EnrollResponse.Fail(PmeError.InvalidCode);
        }

        var used = await db.Devices.CountAsync(d => d.OrganizationId == organization.Id && !d.Revoked, ct).ConfigureAwait(false);
        if (used >= organization.Seats)
        {
            LogRefused("enroll", "NoSeatLeft");
            return EnrollResponse.Fail(PmeError.NoSeatLeft);
        }

        var secret = Secrets.NewDeviceSecret();
        var device = new DeviceEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            MachineName = Secrets.Truncate(request.MachineName.Trim(), PmeProtocol.MaxTextLength),
            SecretHash = Secrets.HashDeviceSecret(secret),
            EnrolledAt = time.GetUtcNow(),
            AppVersion = Secrets.Truncate(request.AppVersion, 32),
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new EnrollResponse(PmeError.None, device.Id, secret, organization.Name);
    }

    /// <summary>Poste authentifié par l'en-tête « identifiant:secret » ; null si inconnu, révoqué ou secret faux.</summary>
    public async Task<(DeviceEntity? Device, PmeError Error)> AuthenticateAsync(string? header, CancellationToken ct)
    {
        var parts = (header ?? string.Empty).Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id))
        {
            return (null, PmeError.Unauthorized);
        }

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        if (device is null || !Secrets.FixedTimeEquals(device.SecretHash, Secrets.HashDeviceSecret(parts[1])))
        {
            LogRefused("report", "Unauthorized");
            return (null, PmeError.Unauthorized);
        }

        return device.Revoked ? (null, PmeError.Revoked) : (device, PmeError.None);
    }

    public async Task<PmeResponse> ReportAsync(string? header, DeviceReport? report, CancellationToken ct)
    {
        var (device, error) = await AuthenticateAsync(header, ct).ConfigureAwait(false);
        if (device is null)
        {
            return new PmeResponse(error);
        }

        if (report is null || report.Score is < 0 or > 100 || report.Issues is null || report.Issues.Count > PmeProtocol.MaxIssues)
        {
            return new PmeResponse(PmeError.InvalidRequest);
        }

        var now = time.GetUtcNow();
        var issues = report.Issues
            .Select(i => new ReportedIssue(Secrets.Truncate(i.Code, 64), i.Severity,
                (i.Args ?? []).Take(PmeProtocol.MaxArgs).Select(a => Secrets.Truncate(a, PmeProtocol.MaxTextLength)).ToList()))
            .ToList();
        device.LastReportAt = now;
        device.AnalyzedAt = report.AnalyzedAt > now ? now : report.AnalyzedAt;
        device.Score = report.Score;
        device.Security = report.SubScores?.Security;
        device.Performance = report.SubScores?.Performance;
        device.Stability = report.SubScores?.Stability;
        device.Storage = report.SubScores?.Storage;
        device.IssuesJson = JsonSerializer.Serialize(issues, PcSanteJson.Options);
        device.WindowsVersion = Secrets.Truncate(report.WindowsVersion, 128);
        device.AppVersion = Secrets.Truncate(report.AppVersion, 32);
        db.ScoreHistory.Add(new ScoreHistoryEntity { DeviceId = device.Id, At = now, Score = report.Score });

        await UpdateAlertsAsync(device, issues, now, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PmeResponse(PmeError.None);
    }

    /// <summary>Signale les postes silencieux et referme l'alerte quand ils répondent de nouveau (appelé périodiquement).</summary>
    public async Task<int> CheckSilentDevicesAsync(CancellationToken ct)
    {
        var limit = time.GetUtcNow() - SilentAfter;
        var devices = await db.Devices.Where(d => !d.Revoked).ToListAsync(ct).ConfigureAwait(false);
        var open = await db.Alerts.Where(a => a.Kind == Silent && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false);
        var created = 0;
        foreach (var device in devices)
        {
            var silent = (device.LastReportAt ?? device.EnrolledAt) < limit;
            var alert = open.FirstOrDefault(a => a.DeviceId == device.Id);
            if (silent && alert is null)
            {
                db.Alerts.Add(new AlertEntity { OrganizationId = device.OrganizationId, DeviceId = device.Id, Kind = Silent, CreatedAt = time.GetUtcNow() });
                created++;
            }
            else if (!silent && alert is not null)
            {
                alert.ResolvedAt = time.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created;
    }

    public static IReadOnlyList<ReportedIssue> IssuesOf(DeviceEntity device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrEmpty(device.IssuesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ReportedIssue>>(device.IssuesJson, PcSanteJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Alertes attendues d'après le rapport : ouvertes si absentes, refermées si la cause a disparu.</summary>
    private async Task UpdateAlertsAsync(DeviceEntity device, IReadOnlyList<ReportedIssue> issues, DateTimeOffset now, CancellationToken ct)
    {
        var expected = new HashSet<(string Kind, string? Detail)>();
        if (device.Score < HealthScoreCalculator.OrangeThreshold)
        {
            expected.Add((ScoreRed, null));
        }

        foreach (var code in issues.Where(i => i.Severity == IssueSeverity.Critical).Select(i => i.Code).Distinct(StringComparer.Ordinal))
        {
            expected.Add((CriticalIssue, code));
        }

        var open = await db.Alerts.Where(a => a.DeviceId == device.Id && a.ResolvedAt == null && a.Kind != Silent).ToListAsync(ct).ConfigureAwait(false);
        foreach (var alert in open.Where(a => !expected.Contains((a.Kind, a.Detail))))
        {
            alert.ResolvedAt = now;
        }

        foreach (var (kind, detail) in expected.Where(e => !open.Any(a => a.Kind == e.Kind && a.Detail == e.Detail)))
        {
            db.Alerts.Add(new AlertEntity { OrganizationId = device.OrganizationId, DeviceId = device.Id, Kind = kind, Detail = detail, CreatedAt = now });
        }

        // Un poste qui répond n'est plus silencieux.
        foreach (var silent in await db.Alerts.Where(a => a.DeviceId == device.Id && a.Kind == Silent && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false))
        {
            silent.ResolvedAt = now;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Console PME : refus {Endpoint} ({Reason})")]
    private partial void LogRefused(string endpoint, string reason);
}
