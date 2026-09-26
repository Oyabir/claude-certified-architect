using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PcSante.Core.Health;
using PcSante.Core.Pme;
using PcSante.PmeConsole.Services;

namespace PcSante.PmeConsole.Tests;

public sealed class ConsoleTests : IDisposable
{
    private readonly ConsoleFactory _console = new();

    public void Dispose() => _console.Dispose();

    [Fact]
    public async Task Inscription_avec_le_code_dans_la_limite_des_postes()
    {
        var org = await _console.CreateOrganizationAsync(seats: 2);

        (await _console.EnrollAsync("PME-00000-00000-00000")).Error.Should().Be(PmeError.InvalidCode);
        (await _console.EnrollAsync("pas un code")).Error.Should().Be(PmeError.InvalidRequest);

        var first = await _console.EnrollAsync(org.EnrollmentCode.ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal));
        first.Ok.Should().BeTrue("tirets et minuscules acceptés");
        first.OrganizationName.Should().Be("Cabinet Test");
        first.Secret.Should().NotBeNullOrEmpty();
        (await _console.EnrollAsync(org.EnrollmentCode, "PC-COMPTA")).Ok.Should().BeTrue();
        (await _console.EnrollAsync(org.EnrollmentCode, "PC-3")).Error.Should().Be(PmeError.NoSeatLeft);
    }

    [Fact]
    public async Task Rapport_accepte_seulement_avec_le_secret_du_poste()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode);

        (await _console.ReportAsync(device, 82)).Ok.Should().BeTrue();
        (await _console.ReportAsync($"{device.DeviceId}:mauvais-secret", 82)).Error.Should().Be(PmeError.Unauthorized);
        (await _console.ReportAsync("n'importe quoi", 82)).Error.Should().Be(PmeError.Unauthorized);
    }

    [Fact]
    public async Task Alertes_ouvertes_puis_refermees_quand_le_poste_va_mieux()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode);
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);

        await _console.ReportAsync(device, 30, new ReportedIssue("RealtimeOff", IssueSeverity.Critical, []));
        var alerts = await web.GetFromJsonAsync<JsonElement>("/api/console/alerts?lang=fr");
        alerts.EnumerateArray().Select(a => a.GetProperty("kind").GetString()).Should().BeEquivalentTo(["ScoreRed", "CriticalIssue"]);
        alerts.EnumerateArray().Should().Contain(a => a.GetProperty("text").GetString()!.Contains("antivirus", StringComparison.OrdinalIgnoreCase));

        await _console.ReportAsync(device, 95);
        (await web.GetFromJsonAsync<JsonElement>("/api/console/alerts")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Poste_silencieux_signale_apres_trois_jours()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode);
        await _console.ReportAsync(device, 90);

        _console.Time.Now += DeviceService.SilentAfter + TimeSpan.FromHours(1);
        using (var scope = _console.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<DeviceService>().CheckSilentDevicesAsync(default)).Should().Be(1);
        }

        await _console.ReportAsync(device, 90);
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);
        (await web.GetFromJsonAsync<JsonElement>("/api/console/alerts")).GetArrayLength().Should().Be(0, "le poste a répondu");
    }

    [Fact]
    public async Task Postes_et_detail_avec_problemes_traduits()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode);
        await _console.ReportAsync(device, 62, new ReportedIssue("LowDiskSpace", IssueSeverity.Warning, ["8"]));
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);

        var devices = await web.GetFromJsonAsync<JsonElement>("/api/console/devices");
        devices.GetArrayLength().Should().Be(1);
        devices[0].GetProperty("color").GetString().Should().Be("Orange");

        var detail = await web.GetFromJsonAsync<JsonElement>($"/api/console/devices/{device.DeviceId}?lang=en");
        detail.GetProperty("issues")[0].GetProperty("code").GetString().Should().Be("LowDiskSpace");
        detail.GetProperty("issues")[0].GetProperty("title").GetString().Should().NotStartWith("Issue_", "titre traduit depuis les fichiers de l'application");
        detail.GetProperty("windowsVersion").GetString().Should().Be("Windows 11 Pro 24H2");
    }

    [Fact]
    public async Task Connexion_verrouillee_apres_cinq_echecs()
    {
        var org = await _console.CreateOrganizationAsync();
        using var client = _console.CreateClient();
        client.DefaultRequestHeaders.Add("X-PcSante-Console", "1");

        for (var i = 0; i < ManagerService.MaxFailedLogins; i++)
        {
            (await client.PostAsJsonAsync("/api/console/login", new { email = org.OwnerEmail, password = "faux" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await client.PostAsJsonAsync("/api/console/login", new { email = org.OwnerEmail, password = org.TemporaryPassword });
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("locked");

        _console.Time.Now += ManagerService.LockDuration + TimeSpan.FromMinutes(1);
        (await client.PostAsJsonAsync("/api/console/login", new { email = org.OwnerEmail, password = org.TemporaryPassword })).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Api_web_exige_la_session_et_l_en_tete_anti_csrf()
    {
        var org = await _console.CreateOrganizationAsync();
        using var anonymous = _console.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-PcSante-Console", "1");
        (await anonymous.GetAsync("/api/console/devices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var noHeader = _console.CreateClient();
        (await noHeader.PostAsJsonAsync("/api/console/login", new { email = org.OwnerEmail, password = org.TemporaryPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "sans l'en-tête, une requête d'un site tiers est refusée");

        var page = await _console.CreateClient().GetAsync("/");
        page.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'");
    }

    [Fact]
    public async Task Lecteur_consulte_mais_ne_gere_pas()
    {
        var org = await _console.CreateOrganizationAsync();
        using var owner = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);
        var added = await owner.PostAsJsonAsync("/api/console/users", new { email = "lecteur@cabinet.ma", displayName = "Lecteur", role = "Viewer" });
        var password = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;

        using var viewer = await _console.SignInAsync("lecteur@cabinet.ma", password);
        (await viewer.GetAsync("/api/console/devices")).IsSuccessStatusCode.Should().BeTrue();
        (await viewer.GetAsync("/api/console/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync("/api/console/organization/enrollment-code", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Une_organisation_ne_voit_jamais_les_postes_d_une_autre()
    {
        var a = await _console.CreateOrganizationAsync("A", "a@a.ma");
        var b = await _console.CreateOrganizationAsync("B", "b@b.ma");
        var deviceOfB = await _console.EnrollAsync(b.EnrollmentCode);
        await _console.ReportAsync(deviceOfB, 20);

        using var webA = await _console.SignInAsync(a.OwnerEmail, a.TemporaryPassword);
        (await webA.GetFromJsonAsync<JsonElement>("/api/console/devices")).GetArrayLength().Should().Be(0);
        (await webA.GetAsync($"/api/console/devices/{deviceOfB.DeviceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await webA.DeleteAsync($"/api/console/devices/{deviceOfB.DeviceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Poste_retire_libere_le_siege_et_ne_peut_plus_envoyer()
    {
        var org = await _console.CreateOrganizationAsync(seats: 1);
        var device = await _console.EnrollAsync(org.EnrollmentCode);
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);

        (await web.DeleteAsync($"/api/console/devices/{device.DeviceId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _console.ReportAsync(device, 80)).Error.Should().Be(PmeError.Revoked);
        (await _console.EnrollAsync(org.EnrollmentCode, "PC-NEUF")).Ok.Should().BeTrue("le siège est libéré");
    }

    [Fact]
    public async Task Nouveau_code_d_inscription_remplace_l_ancien()
    {
        var org = await _console.CreateOrganizationAsync();
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);

        var response = await web.PostAsync("/api/console/organization/enrollment-code", null);
        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrollmentCode").GetString()!;

        (await _console.EnrollAsync(org.EnrollmentCode)).Error.Should().Be(PmeError.InvalidCode);
        (await _console.EnrollAsync(code)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Rapport_consolide_du_mois_et_export_csv()
    {
        var org = await _console.CreateOrganizationAsync();
        var one = await _console.EnrollAsync(org.EnrollmentCode, "PC-1");
        var two = await _console.EnrollAsync(org.EnrollmentCode, "=PC-2");
        await _console.ReportAsync(one, 80);
        await _console.ReportAsync(one, 90);
        await _console.ReportAsync(two, 40);
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);

        var report = await web.GetFromJsonAsync<JsonElement>("/api/console/reports/2026/9");
        report.GetProperty("devices").GetInt32().Should().Be(2);
        report.GetProperty("averageScore").GetDouble().Should().Be(62.5);

        var csv = await web.GetByteArrayAsync("/api/console/reports/2026/9/csv?lang=fr");
        csv.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        System.Text.Encoding.UTF8.GetString(csv).Should().Contain("\"'=PC-2\"", "protection contre l'injection de formules");
    }

    [Fact]
    public async Task Nouvelles_alertes_envoyees_une_seule_fois_aux_gerants()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode, "<b>PC-PIRATE</b>");
        await _console.ReportAsync(device, 30, new ReportedIssue("RealtimeOff", IssueSeverity.Critical, []));

        await _console.RunJobsAsync();
        await _console.RunJobsAsync();

        var mail = _console.Outbox.Mails.Should().ContainSingle("un seul e-mail regroupant les alertes, envoyé une seule fois").Subject;
        mail.To.Should().Equal("gerant@cabinet.ma");
        mail.Subject.Should().Contain("2 nouvelle(s) alerte(s)");
        mail.HtmlBody.Should().NotContain("<b>PC-PIRATE</b>", "le nom du poste est échappé dans le HTML").And.Contain("&lt;b&gt;PC-PIRATE");
    }

    [Fact]
    public async Task Rapport_mensuel_envoye_le_premier_du_mois_avec_le_csv()
    {
        var org = await _console.CreateOrganizationAsync();
        var device = await _console.EnrollAsync(org.EnrollmentCode);
        await _console.ReportAsync(device, 88);

        await _console.RunJobsAsync();
        _console.Outbox.Mails.Should().NotContain(m => m.Subject.Contains("rapport de", StringComparison.Ordinal), "l'organisation vient d'être créée ce mois-ci");

        _console.Time.Now = new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero);
        await _console.RunJobsAsync();
        await _console.RunJobsAsync();

        var report = _console.Outbox.Mails.Where(m => m.Subject.Contains("rapport de", StringComparison.Ordinal)).Should().ContainSingle().Subject;
        report.Subject.Should().Contain("septembre 2026");
        report.AttachmentName.Should().Be("pcsante-pme-2026-09.csv");
        report.Attachment!.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public async Task Sans_serveur_smtp_les_e_mails_sont_deposes_en_fichiers_eml()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pcsante-eml-{Guid.NewGuid():N}");
        var sender = new SmtpMailSender(new EmailOptions { PickupDirectory = folder, From = "console@exemple.ma" },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpMailSender>.Instance);

        (await sender.SendAsync(new OutgoingMail(["gerant@cabinet.ma"], "Essai", "<p>ok</p>"), default)).Should().BeTrue();

        var eml = File.ReadAllText(Directory.GetFiles(folder, "*.eml").Should().ContainSingle().Subject);
        eml.Should().Contain("X-Receiver: gerant@cabinet.ma");
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task Changement_du_mot_de_passe_provisoire()
    {
        var org = await _console.CreateOrganizationAsync();
        using var web = await _console.SignInAsync(org.OwnerEmail, org.TemporaryPassword);
        (await web.GetFromJsonAsync<JsonElement>("/api/console/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        (await web.PostAsJsonAsync("/api/console/me/password", new { current = org.TemporaryPassword, replacement = "court" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await web.PostAsJsonAsync("/api/console/me/password", new { current = org.TemporaryPassword, replacement = "un-mot-de-passe-solide" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await web.GetFromJsonAsync<JsonElement>("/api/console/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
    }
}
