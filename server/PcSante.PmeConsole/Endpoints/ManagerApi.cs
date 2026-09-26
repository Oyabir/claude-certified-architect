using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PcSante.Core.Health;
using PcSante.PmeConsole.Data;
using PcSante.PmeConsole.Services;

namespace PcSante.PmeConsole.Endpoints;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record PasswordRequest(string? Current, string? Replacement);

public sealed record NewManagerRequest(string? Email, string? DisplayName, ManagerRole Role);

public sealed record IssueView(string Code, IssueSeverity Severity, string Title, string Why);

public sealed record DeviceView(Guid Id, string MachineName, int? Score, HealthColor? Color, DateTimeOffset? LastReportAt, bool Silent, int OpenAlerts);

public sealed record DeviceDetail(DeviceView Device, int? Security, int? Performance, int? Stability, int? Storage, string? WindowsVersion,
    string? AppVersion, DateTimeOffset EnrolledAt, IReadOnlyList<IssueView> Issues);

public sealed record AlertView(long Id, Guid DeviceId, string MachineName, string Kind, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// API de l'interface web des gérants (/api/console). Session par cookie (HttpOnly, SameSite=Strict) ; chaque
/// requête porte l'en-tête X-PcSante-Console, qu'un site tiers ne peut pas envoyer (protection CSRF).
/// </summary>
public static class ManagerApi
{
    public const string RateLimitPolicy = "managers";
    public const string CsrfHeader = "X-PcSante-Console";
    public const string OrganizationClaim = "pcsante:org";
    public const string OwnerPolicy = "owner";

    public static void MapManagerApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/api/console").RequireRateLimiting(RateLimitPolicy).AddEndpointFilter(RequireCsrfHeader);

        api.MapPost("/login", LoginAsync).AllowAnonymous();
        api.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.NoContent();
        }).AllowAnonymous();

        var signedIn = api.MapGroup(string.Empty).RequireAuthorization();
        signedIn.MapGet("/me", MeAsync);
        signedIn.MapPost("/me/password", async (PasswordRequest request, ClaimsPrincipal user, ManagerService managers, CancellationToken ct) =>
            await managers.ChangePasswordAsync(ManagerId(user), request.Current, request.Replacement, ct).ConfigureAwait(false)
                ? Results.NoContent()
                : Results.BadRequest(new { error = "password" }));
        signedIn.MapGet("/overview", OverviewAsync);
        signedIn.MapGet("/devices", DevicesAsync);
        signedIn.MapGet("/devices/{id:guid}", DeviceAsync);
        signedIn.MapGet("/alerts", AlertsAsync);
        signedIn.MapGet("/reports/{year:int}/{month:int}", async (int year, int month, ClaimsPrincipal user, ReportService reports, CancellationToken ct) =>
            await reports.BuildAsync(OrganizationId(user), year, month, ct).ConfigureAwait(false) is { } report ? Results.Ok(report) : Results.NotFound());
        signedIn.MapGet("/reports/{year:int}/{month:int}/csv", async (int year, int month, string? lang, ClaimsPrincipal user, ReportService reports, CancellationToken ct) =>
            await reports.BuildAsync(OrganizationId(user), year, month, ct).ConfigureAwait(false) is { } report
                ? Results.File(ReportService.ToCsv(report, ConsoleStrings.CultureOf(lang)), "text/csv", string.Create(CultureInfo.InvariantCulture, $"pcsante-pme-{year:0000}-{month:00}.csv"))
                : Results.NotFound());

        // Gestion : gérants seulement.
        var owner = api.MapGroup(string.Empty).RequireAuthorization(OwnerPolicy);
        owner.MapDelete("/devices/{id:guid}", RevokeDeviceAsync);
        owner.MapPost("/alerts/{id:long}/resolve", ResolveAlertAsync);
        owner.MapGet("/users", UsersAsync);
        owner.MapPost("/users", async (NewManagerRequest request, ClaimsPrincipal user, ManagerService managers, CancellationToken ct) =>
            await managers.AddManagerAsync(OrganizationId(user), request.Email ?? string.Empty, request.DisplayName ?? string.Empty, request.Role, ct).ConfigureAwait(false) is { } password
                ? Results.Ok(new { temporaryPassword = password })
                : Results.BadRequest(new { error = "user" }));
        owner.MapDelete("/users/{id:guid}", async (Guid id, ClaimsPrincipal user, ManagerService managers, CancellationToken ct) =>
            await managers.RemoveManagerAsync(OrganizationId(user), id, ManagerId(user), ct).ConfigureAwait(false) ? Results.NoContent() : Results.BadRequest(new { error = "user" }));
        owner.MapPost("/organization/enrollment-code", async (ClaimsPrincipal user, ManagerService managers, CancellationToken ct) =>
            await managers.RotateEnrollmentCodeAsync(OrganizationId(user), ct).ConfigureAwait(false) is { } code
                ? Results.Ok(new { enrollmentCode = code })
                : Results.NotFound());
    }

    internal static Guid OrganizationId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(OrganizationClaim)!);

    internal static Guid ManagerId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async ValueTask<object?> RequireCsrfHeader(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers[CsrfHeader] == "1" ? await next(context).ConfigureAwait(false) : Results.StatusCode(StatusCodes.Status403Forbidden);

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext http, ManagerService managers, CancellationToken ct)
    {
        var (outcome, manager) = await managers.LoginAsync(request.Email, request.Password, ct).ConfigureAwait(false);
        if (manager is null)
        {
            return Results.Json(new { error = outcome == LoginOutcome.Locked ? "locked" : "invalid" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, manager.Id.ToString()),
            new Claim(ClaimTypes.Name, manager.DisplayName),
            new Claim(ClaimTypes.Role, manager.Role.ToString()),
            new Claim(OrganizationClaim, manager.OrganizationId.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);
        return Results.Ok(new { manager.DisplayName, role = manager.Role, manager.MustChangePassword });
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal user, ConsoleDbContext db, CancellationToken ct)
    {
        var id = ManagerId(user);
        var manager = await db.Managers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        return manager is null ? Results.Unauthorized() : Results.Ok(new { manager.DisplayName, manager.Email, role = manager.Role, manager.MustChangePassword });
    }

    private static async Task<IResult> OverviewAsync(ClaimsPrincipal user, ConsoleDbContext db, TimeProvider time, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, ct).ConfigureAwait(false);
        var devices = await db.Devices.AsNoTracking().Where(d => d.OrganizationId == orgId && !d.Revoked).ToListAsync(ct).ConfigureAwait(false);
        var alerts = await db.Alerts.AsNoTracking().CountAsync(a => a.OrganizationId == orgId && a.ResolvedAt == null, ct).ConfigureAwait(false);
        var scores = devices.Where(d => d.Score is not null).Select(d => d.Score!.Value).ToList();
        return Results.Ok(new
        {
            organization.Name,
            organization.Seats,
            devices = devices.Count,
            averageScore = scores.Count == 0 ? (double?)null : Math.Round(scores.Average(), 1),
            openAlerts = alerts,
            enrollmentCodeHint = organization.EnrollmentCodeHint,
            red = devices.Count(d => d.Score < HealthScoreCalculator.OrangeThreshold),
            silent = devices.Count(d => IsSilent(d, time)),
        });
    }

    private static async Task<IResult> DevicesAsync(ClaimsPrincipal user, ConsoleDbContext db, TimeProvider time, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var devices = await db.Devices.AsNoTracking().Where(d => d.OrganizationId == orgId && !d.Revoked).ToListAsync(ct).ConfigureAwait(false);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.OrganizationId == orgId && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(devices.Select(d => View(d, time, alerts.Count(a => a.DeviceId == d.Id)))
            .OrderBy(d => d.Score ?? 101).ThenBy(d => d.MachineName, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static async Task<IResult> DeviceAsync(Guid id, string? lang, ClaimsPrincipal user, ConsoleDbContext db, TimeProvider time, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId && !d.Revoked, ct).ConfigureAwait(false);
        if (device is null)
        {
            return Results.NotFound();
        }

        var culture = ConsoleStrings.CultureOf(lang);
        var alerts = await db.Alerts.AsNoTracking().CountAsync(a => a.DeviceId == id && a.ResolvedAt == null, ct).ConfigureAwait(false);
        var issues = DeviceService.IssuesOf(device).Select(i =>
        {
            var args = i.Args.Cast<object>().ToArray();
            return new IssueView(i.Code, i.Severity, ConsoleStrings.Text($"Issue_{i.Code}_Title", culture, args), ConsoleStrings.Text($"Issue_{i.Code}_Why", culture, args));
        }).ToList();
        return Results.Ok(new DeviceDetail(View(device, time, alerts), device.Security, device.Performance, device.Stability, device.Storage,
            device.WindowsVersion, device.AppVersion, device.EnrolledAt, issues));
    }

    private static async Task<IResult> AlertsAsync(string? lang, ClaimsPrincipal user, ConsoleDbContext db, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var culture = ConsoleStrings.CultureOf(lang);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.OrganizationId == orgId && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false);
        var names = await db.Devices.AsNoTracking().Where(d => d.OrganizationId == orgId).ToDictionaryAsync(d => d.Id, d => d.MachineName, ct).ConfigureAwait(false);
        return Results.Ok(alerts.OrderByDescending(a => a.CreatedAt).Select(a =>
            new AlertView(a.Id, a.DeviceId, names.GetValueOrDefault(a.DeviceId, "?"), a.Kind, AlertText(a, culture), a.CreatedAt)).ToList());
    }

    internal static string AlertText(AlertEntity alert, CultureInfo culture) => alert.Kind switch
    {
        DeviceService.CriticalIssue => ConsoleStrings.Text("Pme_Alert_CriticalIssue", culture, ConsoleStrings.Text($"Issue_{alert.Detail}_Title", culture, "…")),
        _ => ConsoleStrings.Text($"Pme_Alert_{alert.Kind}", culture),
    };

    private static async Task<IResult> RevokeDeviceAsync(Guid id, ClaimsPrincipal user, ConsoleDbContext db, TimeProvider time, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId && !d.Revoked, ct).ConfigureAwait(false);
        if (device is null)
        {
            return Results.NotFound();
        }

        // Le poste est retiré (son secret ne vaut plus rien) : le siège est libéré.
        device.Revoked = true;
        foreach (var alert in await db.Alerts.Where(a => a.DeviceId == id && a.ResolvedAt == null).ToListAsync(ct).ConfigureAwait(false))
        {
            alert.ResolvedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ResolveAlertAsync(long id, ClaimsPrincipal user, ConsoleDbContext db, TimeProvider time, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == orgId && a.ResolvedAt == null, ct).ConfigureAwait(false);
        if (alert is null)
        {
            return Results.NotFound();
        }

        alert.ResolvedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> UsersAsync(ClaimsPrincipal user, ConsoleDbContext db, CancellationToken ct)
    {
        var orgId = OrganizationId(user);
        var managers = await db.Managers.AsNoTracking().Where(m => m.OrganizationId == orgId).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(managers.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new { m.Id, m.DisplayName, m.Email, role = m.Role, m.LastLoginAt }).ToList());
    }

    private static bool IsSilent(DeviceEntity device, TimeProvider time) =>
        (device.LastReportAt ?? device.EnrolledAt) < time.GetUtcNow() - DeviceService.SilentAfter;

    private static DeviceView View(DeviceEntity d, TimeProvider time, int openAlerts) =>
        new(d.Id, d.MachineName, d.Score, d.Score is { } s ? HealthScoreCalculator.ColorOf(s) : null, d.LastReportAt, IsSilent(d, time), openAlerts);
}
