using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PcSante.Core.Licensing;
using PcSante.LicenseServer.Data;
using PcSante.LicenseServer.Services;

namespace PcSante.LicenseServer.Endpoints;

public sealed record GenerateKeysRequest(int Count, LicenseTier Tier, int? Seats, int? ValidDays, string? Note);

public sealed record RevokeRequest(string? Reason);

public sealed record ActivationView(Guid Id, DateTimeOffset ActivatedAt, DateTimeOffset LastSeenAt, DateTimeOffset? DeactivatedAt, string? DeactivationReason, string? AppVersion);

public sealed record LicenseView(
    Guid Id,
    string KeyHint,
    LicenseTier Tier,
    int Seats,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    int TransfersRemaining,
    string? Note,
    IReadOnlyList<ActivationView> Activations);

/// <summary>
/// Administration minimale (section 12) : générer des clés, voir les activations, révoquer, remettre à zéro un transfert.
/// Protégée par la clé d'administration (en-tête X-Admin-Key), comparée en temps constant.
/// </summary>
public static class AdminApi
{
    public const string RateLimitPolicy = "admin";
    public const string HeaderName = "X-Admin-Key";

    public static void MapAdminApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/api").RequireRateLimiting(RateLimitPolicy).AddEndpointFilter(RequireAdminKey);

        admin.MapPost("/licenses", async (GenerateKeysRequest request, LicenseService service, CancellationToken ct) =>
        {
            var seats = request.Seats ?? (request.Tier == LicenseTier.Family ? 3 : 1);
            try
            {
                var keys = await service.GenerateKeysAsync(request.Count, request.Tier, seats, request.ValidDays, request.Note, ct).ConfigureAwait(false);
                return Results.Ok(new { keys });
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.BadRequest(new { error = "Paramètres invalides (1 à 500 clés, offre Premium ou Family, 1 à 50 postes, 1 à 3650 jours)." });
            }
        });

        admin.MapGet("/licenses", async (string? hint, LicenseDbContext db, LicenseService service, TimeProvider time, CancellationToken ct) =>
        {
            var query = db.Licenses.Include(l => l.Activations).Include(l => l.Transfers).AsNoTracking();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var h = "…" + hint.Trim().TrimStart('…').ToUpperInvariant();
                query = query.Where(l => l.KeyHint == h);
            }

            var now = time.GetUtcNow();
            var list = await query.OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(list.Select(l => new LicenseView(
                l.Id, l.KeyHint, l.Tier, l.Seats, l.CreatedAt, l.ExpiresAt, l.RevokedAt, l.RevokedReason,
                service.RemainingTransfers(l, now), l.Note,
                l.Activations.OrderByDescending(a => a.ActivatedAt)
                    .Select(a => new ActivationView(a.Id, a.ActivatedAt, a.LastSeenAt, a.DeactivatedAt, a.DeactivationReason, a.AppVersion))
                    .ToList())));
        });

        admin.MapPost("/licenses/{id:guid}/revoke", async (Guid id, RevokeRequest? request, LicenseService service, CancellationToken ct) =>
            await service.RevokeAsync(id, request?.Reason, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/licenses/{id:guid}/reset-transfers", async (Guid id, LicenseService service, CancellationToken ct) =>
            await service.ResetTransfersAsync(id, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound());

        admin.MapGet("/refused", async (int? limit, LicenseDbContext db, CancellationToken ct) =>
            Results.Ok(await db.RefusedAttempts.AsNoTracking()
                .OrderByDescending(r => r.At).Take(Math.Clamp(limit ?? 200, 1, 1000))
                .ToListAsync(ct).ConfigureAwait(false)));
    }

    private static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var keys = context.HttpContext.RequestServices.GetRequiredService<ServerKeys>();
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!FixedTimeEquals(provided, keys.AdminApiKey))
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Admin");
            AdminLog.Unauthorized(logger, PublicApi.Ip(context.HttpContext) ?? "-");
            return Results.Unauthorized();
        }

        return await next(context).ConfigureAwait(false);
    }

    internal static bool FixedTimeEquals(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}

internal static partial class AdminLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Accès administration refusé depuis {Ip}")]
    public static partial void Unauthorized(ILogger logger, string ip);
}
