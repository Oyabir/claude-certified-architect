using System.Net.Http.Json;
using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Pme;
using PcSante.Licensing;

namespace PcSante.Service.Pme;

/// <summary>Inscription de ce poste à une console PME (secret du poste chiffré par DPAPI machine).</summary>
[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed record PmeEnrollment(Guid DeviceId, string Secret, string OrganizationName, DateTimeOffset EnrolledAt, DateTimeOffset? LastReportAt, PmeError LastError);

/// <summary>État visible par l'interface (jamais le secret).</summary>
public sealed record PmeStatus(bool Available, bool Enrolled, string? OrganizationName, DateTimeOffset? LastReportAt, PmeError LastError);

/// <summary>Appels à la console PME ; null si elle est injoignable (le poste réessaiera).</summary>
public interface IPmeClient
{
    Task<EnrollResponse?> EnrollAsync(EnrollRequest request, CancellationToken cancellationToken);

    Task<PmeResponse?> ReportAsync(Guid deviceId, string secret, DeviceReport report, CancellationToken cancellationToken);
}

public sealed class HttpPmeClient(HttpClient http) : IPmeClient
{
    public Task<EnrollResponse?> EnrollAsync(EnrollRequest request, CancellationToken cancellationToken) =>
        PostAsync<EnrollRequest, EnrollResponse>(PmeProtocol.EnrollPath, request, null, cancellationToken);

    public Task<PmeResponse?> ReportAsync(Guid deviceId, string secret, DeviceReport report, CancellationToken cancellationToken) =>
        PostAsync<DeviceReport, PmeResponse>(PmeProtocol.ReportPath, report, $"{deviceId}:{secret}", cancellationToken);

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, string? credentials, CancellationToken cancellationToken)
    {
        if (http.BaseAddress is null)
        {
            return default;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body, options: PcSanteJson.Options) };
            if (credentials is not null)
            {
                request.Headers.Add(PmeProtocol.DeviceHeader, credentials);
            }

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TResponse>(PcSanteJson.Options, cancellationToken).ConfigureAwait(false)
                : default;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return default;
        }
    }
}

/// <summary>Stockage chiffré (DPAPI machine) de l'inscription : illisible sur un autre PC.</summary>
public sealed class PmeEnrollmentStore(string filePath, ISecretProtector protector)
{
    public PmeEnrollment? Load()
    {
        try
        {
            if (!File.Exists(filePath) || protector.Unprotect(File.ReadAllBytes(filePath)) is not { } data)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PmeEnrollment>(data, PcSanteJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(PmeEnrollment enrollment)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + ".tmp";
        File.WriteAllBytes(temp, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(enrollment, PcSanteJson.Options)));
        File.Move(temp, filePath, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
