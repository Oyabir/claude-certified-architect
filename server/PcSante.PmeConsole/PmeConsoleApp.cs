using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PcSante.PmeConsole.Data;
using PcSante.PmeConsole.Endpoints;
using PcSante.PmeConsole.Services;
using Serilog;

namespace PcSante.PmeConsole;

public static class PmeConsoleApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var config = builder.Configuration;

        builder.Host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(context.Configuration["Logging:Directory"] ?? "logs", "pmeconsole-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<DeviceService>();
        builder.Services.AddScoped<ManagerService>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddScoped<ConsoleJobs>();
        builder.Services.AddSingleton(config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions());
        builder.Services.AddSingleton<IMailSender, SmtpMailSender>();
        if (config.GetValue("Jobs:Enabled", true))
        {
            builder.Services.AddHostedService<ConsoleJobsWorker>();
        }
        AddDatabase(builder.Services, config);

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "pcsante-console";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // En production, la console est derrière un proxy HTTPS (X-Forwarded-Proto) : le cookie devient Secure.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            // API : pas de redirection vers une page de connexion, un code HTTP clair.
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ManagerApi.OwnerPolicy, policy => policy.RequireRole(nameof(ManagerRole.Owner)));

        var devicesPerMinute = config.GetValue("RateLimiting:DevicesPerMinute", 30);
        var managersPerMinute = config.GetValue("RateLimiting:ManagersPerMinute", 120);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(DeviceApi.RateLimitPolicy, http => Limit(http, devicesPerMinute));
            options.AddPolicy(ManagerApi.RateLimitPolicy, http => Limit(http, managersPerMinute));
        });
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ConsoleDbContext>().Database.EnsureCreated();
        }

        app.UseForwardedHeaders();
        app.Use(SecurityHeaders);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapDeviceApi();
        app.MapManagerApi();
        return app;
    }

    internal static void AddDatabase(IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connection = config.GetConnectionString("Console") ?? "Data Source=pmeconsole.db";
        services.AddDbContext<ConsoleDbContext>(options =>
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
    }

    /// <summary>Politique de sécurité de contenu stricte : scripts et styles uniquement servis par la console.</summary>
    private static Task SecurityHeaders(HttpContext context, Func<Task> next)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        return next();
    }

    private static RateLimitPartition<string> Limit(HttpContext http, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
