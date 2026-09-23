using PcSante.LicenseServer.Services;
using PcSante.Licensing;

namespace PcSante.LicenseServer.Endpoints;

/// <summary>API publique utilisée par l'application : activer, revalider, transférer, désactiver.</summary>
public static class PublicApi
{
    public const string RateLimitPolicy = "public";

    public static void MapPublicApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/").RequireRateLimiting(RateLimitPolicy);

        api.MapPost(LicenseProtocol.ActivatePath, (ActivationRequest request, LicenseService service, HttpContext http, CancellationToken ct) =>
            Respond(service.ActivateAsync(request, Ip(http), ct)));

        api.MapPost(LicenseProtocol.RevalidatePath, (RevalidationRequest request, LicenseService service, HttpContext http, CancellationToken ct) =>
            Respond(service.RevalidateAsync(request, Ip(http), ct)));

        api.MapPost(LicenseProtocol.TransferPath, (TransferRequest request, LicenseService service, HttpContext http, CancellationToken ct) =>
            Respond(service.TransferAsync(request, Ip(http), ct)));

        api.MapPost(LicenseProtocol.DeactivatePath, (DeactivationRequest request, LicenseService service, HttpContext http, CancellationToken ct) =>
            Respond(service.DeactivateAsync(request, Ip(http), ct)));

        api.MapGet(LicenseProtocol.UpdatePath, (UpdateManifestService updates) =>
            updates.GetSigned() is { } manifest ? Results.Ok(manifest) : Results.NoContent());

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    internal static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static async Task<IResult> Respond(Task<LicenseServerResponse> task)
    {
        var response = await task.ConfigureAwait(false);
        return Results.Json(response, Core.PcSanteJson.Options);
    }
}
