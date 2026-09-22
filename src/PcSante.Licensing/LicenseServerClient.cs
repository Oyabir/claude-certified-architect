using System.Net;
using System.Net.Http.Json;
using PcSante.Core;

namespace PcSante.Licensing;

/// <summary>Appels au serveur de licences.</summary>
public interface ILicenseServerClient
{
    Task<LicenseServerResponse?> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken);

    Task<LicenseServerResponse?> RevalidateAsync(RevalidationRequest request, CancellationToken cancellationToken);

    Task<LicenseServerResponse?> TransferAsync(TransferRequest request, CancellationToken cancellationToken);

    Task<LicenseServerResponse?> DeactivateAsync(DeactivationRequest request, CancellationToken cancellationToken);

    Task<SignedUpdateManifest?> GetLatestUpdateAsync(CancellationToken cancellationToken);
}

/// <summary>Client HTTPS. Renvoie null si le serveur est injoignable (fonctionnement hors ligne).</summary>
public sealed class HttpLicenseServerClient(HttpClient http) : ILicenseServerClient
{
    public Task<LicenseServerResponse?> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken) =>
        PostAsync(LicenseProtocol.ActivatePath, request, cancellationToken);

    public Task<LicenseServerResponse?> RevalidateAsync(RevalidationRequest request, CancellationToken cancellationToken) =>
        PostAsync(LicenseProtocol.RevalidatePath, request, cancellationToken);

    public Task<LicenseServerResponse?> TransferAsync(TransferRequest request, CancellationToken cancellationToken) =>
        PostAsync(LicenseProtocol.TransferPath, request, cancellationToken);

    public Task<LicenseServerResponse?> DeactivateAsync(DeactivationRequest request, CancellationToken cancellationToken) =>
        PostAsync(LicenseProtocol.DeactivatePath, request, cancellationToken);

    public async Task<SignedUpdateManifest?> GetLatestUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(LicenseProtocol.UpdatePath, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SignedUpdateManifest>(PcSanteJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<LicenseServerResponse?> PostAsync<T>(string path, T request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(path, UriKind.Relative), request, PcSanteJson.Options, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return LicenseServerResponse.Fail(LicenseErrorCode.RateLimited, DateTimeOffset.UtcNow);
            }

            if ((int)response.StatusCode >= 500)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LicenseServerResponse>(PcSanteJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
