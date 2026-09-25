global using FluentAssertions;
global using Xunit;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PcSante.Core;
using PcSante.Core.Health;
using PcSante.Core.Pme;
using PcSante.PmeConsole.Services;

namespace PcSante.PmeConsole.Tests;

public sealed class AdjustableTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Console PME réelle (base SQLite temporaire, horloge réglable).</summary>
public sealed class ConsoleFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pcsante-console-{Guid.NewGuid():N}.db");

    public AdjustableTime Time { get; } = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("ConnectionStrings:Console", $"Data Source={_dbPath}");
        builder.UseSetting("RateLimiting:DevicesPerMinute", "1000");
        builder.UseSetting("RateLimiting:ManagersPerMinute", "1000");
        builder.UseSetting("Logging:Directory", Path.Combine(Path.GetTempPath(), "pcsante-console-logs"));
        builder.UseSetting("Email:PickupDirectory", Path.Combine(Path.GetTempPath(), $"pcsante-console-mails-{Guid.NewGuid():N}"));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    public async Task<NewOrganization> CreateOrganizationAsync(string name = "Cabinet Test", string email = "gerant@cabinet.ma", int seats = 2)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ManagerService>().CreateOrganizationAsync(name, email, seats, default);
    }

    public async Task<EnrollResponse> EnrollAsync(string code, string machine = "PC-ACCUEIL")
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(PmeProtocol.EnrollPath, new EnrollRequest(code, machine, "1.0.0"), PcSanteJson.Options);
        return (await response.Content.ReadFromJsonAsync<EnrollResponse>(PcSanteJson.Options))!;
    }

    public async Task<PmeResponse> ReportAsync(EnrollResponse device, int score, params ReportedIssue[] issues) =>
        await ReportAsync($"{device.DeviceId}:{device.Secret}", score, issues);

    public async Task<PmeResponse> ReportAsync(string credentials, int score, params ReportedIssue[] issues)
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(PmeProtocol.DeviceHeader, credentials);
        var report = new DeviceReport(Time.Now, score, new SubScores(score, 100, 100, 100), issues, "Windows 11 Pro 24H2", "1.0.0");
        var response = await client.PostAsJsonAsync(PmeProtocol.ReportPath, report, PcSanteJson.Options);
        return (await response.Content.ReadFromJsonAsync<PmeResponse>(PcSanteJson.Options))!;
    }

    /// <summary>Client web connecté (cookie + en-tête anti-CSRF).</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-PcSante-Console", "1");
        var response = await client.PostAsJsonAsync("/api/console/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Fichier temporaire : ignoré.
        }
    }
}
