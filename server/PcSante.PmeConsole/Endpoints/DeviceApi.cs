using PcSante.Core;
using PcSante.Core.Pme;
using PcSante.PmeConsole.Services;

namespace PcSante.PmeConsole.Endpoints;

/// <summary>API des postes : inscription et rapports (authentification par secret du poste, pas de cookie).</summary>
public static class DeviceApi
{
    public const string RateLimitPolicy = "devices";

    public static void MapDeviceApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/").RequireRateLimiting(RateLimitPolicy);

        api.MapPost(PmeProtocol.EnrollPath, async (EnrollRequest request, DeviceService devices, CancellationToken ct) =>
            Results.Json(await devices.EnrollAsync(request, ct).ConfigureAwait(false), PcSanteJson.Options));

        api.MapPost(PmeProtocol.ReportPath, async (DeviceReport report, HttpContext http, DeviceService devices, CancellationToken ct) =>
            Results.Json(await devices.ReportAsync(http.Request.Headers[PmeProtocol.DeviceHeader], report, ct).ConfigureAwait(false), PcSanteJson.Options));

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }
}
