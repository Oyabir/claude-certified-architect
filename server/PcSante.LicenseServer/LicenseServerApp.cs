using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PcSante.LicenseServer.Data;
using PcSante.LicenseServer.Endpoints;
using PcSante.LicenseServer.Services;
using PcSante.Licensing;
using Serilog;

namespace PcSante.LicenseServer;

public static class LicenseServerApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.Host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(context.Configuration["Logging:Directory"] ?? "logs", "licenseserver-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_ => ServerKeys.FromEnvironment(name =>
            builder.Configuration[name] ?? Environment.GetEnvironmentVariable(name)));
        builder.Services.AddSingleton(builder.Configuration.GetSection("Updates").Get<UpdateOptions>() ?? new UpdateOptions());
        builder.Services.AddSingleton<UpdateManifestService>();
        builder.Services.AddScoped<LicenseService>();

        var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        var connection = builder.Configuration.GetConnectionString("Licenses") ?? "Data Source=licenses.db";
        builder.Services.AddDbContext<LicenseDbContext>(options =>
        {
            if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connection);
            }
            else
            {
                options.UseSqlite(connection);
            }
        });

        var publicPerMinute = builder.Configuration.GetValue("RateLimiting:PublicPerMinute", 20);
        var adminPerMinute = builder.Configuration.GetValue("RateLimiting:AdminPerMinute", 120);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PublicApi.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                PublicApi.Ip(http) ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = publicPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(AdminApi.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                PublicApi.Ip(http) ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = adminPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Derrière un proxy inverse (nginx, Caddy) : l'IP réelle sert à la limitation par IP.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        var app = builder.Build();

        // Vérifie la présence des clés au démarrage (échec immédiat plutôt qu'à la première requête).
        _ = app.Services.GetRequiredService<ServerKeys>();
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<LicenseDbContext>().Database.EnsureCreated();
        }

        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapPublicApi();
        app.MapAdminApi();
        return app;
    }

    /// <summary>Commande « keygen » : génère une paire Ed25519 et une clé d'administration, sans rien écrire sur disque.</summary>
    public static void PrintNewKeys(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (privateKey, publicKey) = Ed25519.GenerateKeyPair();
        var admin = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(36));
        output.WriteLine("# À conserver dans un gestionnaire de secrets. NE JAMAIS committer.");
        output.WriteLine($"{ServerKeys.PrivateKeyVariable}={Convert.ToBase64String(privateKey)}");
        output.WriteLine($"{ServerKeys.AdminKeyVariable}={admin}");
        output.WriteLine();
        output.WriteLine("# Clé publique à placer dans branding.props (PcSanteLicensePublicKey) :");
        output.WriteLine(Convert.ToBase64String(publicKey));
    }
}
