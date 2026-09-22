using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PcSante.Core.Licensing;
using PcSante.Licensing;

namespace PcSante.LicenseServer.Tests;

/// <summary>Horloge partagée client/serveur, réglable dans les deux sens.</summary>
public sealed class AdjustableTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}

public sealed class FakeHardware(HardwareIdentity identity) : IHardwareInfoProvider
{
    public HardwareIdentity Identity { get; set; } = identity;

    public HardwareIdentity Read() => Identity;
}

/// <summary>Serveur de licences réel (en mémoire) avec une paire de clés de test générée à la volée.</summary>
public sealed class LicenseServerFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "cle-admin-de-test-suffisamment-longue-0123456789";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pcsante-licenses-{Guid.NewGuid():N}.db");

    public LicenseServerFactory(int publicPerMinute = 1000)
    {
        var (privateKey, publicKey) = Ed25519.GenerateKeyPair();
        PrivateKey = privateKey;
        PublicKeyBase64 = Convert.ToBase64String(publicKey);
        PublicPerMinute = publicPerMinute;
    }

    public byte[] PrivateKey { get; }

    public string PublicKeyBase64 { get; }

    public int PublicPerMinute { get; }

    public AdjustableTime Time { get; } = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));

    public Dictionary<string, string?> ExtraSettings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting(PcSante.LicenseServer.Services.ServerKeys.PrivateKeyVariable, Convert.ToBase64String(PrivateKey));
        builder.UseSetting(PcSante.LicenseServer.Services.ServerKeys.AdminKeyVariable, AdminKey);
        builder.UseSetting("ConnectionStrings:Licenses", $"Data Source={_dbPath}");
        builder.UseSetting("RateLimiting:PublicPerMinute", PublicPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Logging:Directory", Path.Combine(Path.GetTempPath(), "pcsante-server-logs"));
        foreach (var (k, v) in ExtraSettings)
        {
            builder.UseSetting(k, v);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    public HttpClient Admin()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        return client;
    }

    public LicenseManager ClientFor(HardwareIdentity identity, ILicenseStateStore? store = null) =>
        new(new HttpLicenseServerClient(CreateClient()), store ?? new InMemoryLicenseStateStore(), new FakeHardware(identity), Time, PublicKeyBase64);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
