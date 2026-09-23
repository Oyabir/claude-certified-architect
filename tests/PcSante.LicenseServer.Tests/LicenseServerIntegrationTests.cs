using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Licensing;
using PcSante.LicenseServer.Endpoints;
using PcSante.Licensing;

namespace PcSante.LicenseServer.Tests;

public sealed class LicenseServerIntegrationTests : IDisposable
{
    private static readonly HardwareIdentity Pc1 = new("BOARD-1", "DISK-1", "CPU-1", "GUID-1");
    private static readonly HardwareIdentity Pc2 = new("BOARD-2", "DISK-2", "CPU-2", "GUID-2");
    private static readonly HardwareIdentity Pc3 = new("BOARD-3", "DISK-3", "CPU-3", "GUID-3");

    private readonly LicenseServerFactory _factory = new();

    private async Task<(Guid Id, string Key)> CreateLicenseAsync(LicenseTier tier = LicenseTier.Premium, int? seats = null, int? validDays = 365)
    {
        using var admin = _factory.Admin();
        var response = await admin.PostAsJsonAsync("/admin/api/licenses", new GenerateKeysRequest(1, tier, seats, validDays, "test"), PcSanteJson.Options);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var key = body.GetProperty("keys")[0].GetString()!;
        var list = await admin.GetFromJsonAsync<List<LicenseView>>($"/admin/api/licenses?hint={key[^4..]}", PcSanteJson.Options);
        return (list!.Single().Id, key);
    }

    [Fact]
    public async Task Activation_sur_un_pc()
    {
        var (_, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);

        var result = await pc1.ActivateAsync(key, default);

        result.Success.Should().BeTrue();
        result.Status.State.Should().Be(LicenseState.Active);
        result.Status.EffectiveTier.Should().Be(LicenseTier.Premium);
        result.Status.KeyHint.Should().Be(LicenseKeyFormat.Hint(key));
        result.Status.LicenseExpiresAt.Should().Be(_factory.Time.Now.AddDays(365));
    }

    [Fact]
    public async Task Licence_activee_refusee_sur_un_second_pc()
    {
        var (_, key) = await CreateLicenseAsync();
        (await _factory.ClientFor(Pc1).ActivateAsync(key, default)).Success.Should().BeTrue();

        var second = await _factory.ClientFor(Pc2).ActivateAsync(key, default);

        second.Success.Should().BeFalse();
        second.Reason.Should().Be(FailureReason.LicenseUsedElsewhere);
        second.MessageKey.Should().Be("License_UsedElsewhere");
        second.Status.IsPremium.Should().BeFalse();

        using var admin = _factory.Admin();
        var refused = await admin.GetFromJsonAsync<JsonElement>("/admin/api/refused");
        refused.EnumerateArray().Should().Contain(r => r.GetProperty("reason").GetString() == "AlreadyUsedElsewhere");
    }

    [Fact]
    public async Task Reinstallation_meme_pc_sans_nouvelle_activation()
    {
        var (_, key) = await CreateLicenseAsync();
        await _factory.ClientFor(Pc1).ActivateAsync(key, default);

        var reinstall = await _factory.ClientFor(Pc1 with { SystemDiskSerial = "NOUVEAU-DISQUE" }).ActivateAsync(key, default);

        reinstall.Success.Should().BeTrue("3 éléments sur 4 correspondent");
        var license = await GetLicenseAsync(key);
        license.Activations.Should().ContainSingle("la réinstallation ne compte pas une nouvelle activation");
    }

    [Fact]
    public async Task Transfert_desactive_l_ancien_pc_et_limite_a_2_par_an()
    {
        var (id, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);
        await pc1.ActivateAsync(key, default);

        var t1 = await _factory.ClientFor(Pc2).TransferAsync(key, default);
        t1.Success.Should().BeTrue();
        t1.TransfersRemaining.Should().Be(1);

        // L'ancien PC est désactivé : sa revalidation échoue.
        _factory.Time.Advance(TimeSpan.FromDays(8));
        (await pc1.RevalidateAsync(default)).Success.Should().BeFalse();

        (await _factory.ClientFor(Pc3).TransferAsync(key, default)).TransfersRemaining.Should().Be(0);
        var third = await _factory.ClientFor(Pc1).TransferAsync(key, default);
        third.Reason.Should().Be(FailureReason.LicenseTransferLimit);

        // Remise à zéro par l'administrateur.
        using var admin = _factory.Admin();
        (await admin.PostAsync($"/admin/api/licenses/{id}/reset-transfers", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        (await _factory.ClientFor(Pc1).TransferAsync(key, default)).Success.Should().BeTrue();

        // Après un an, les transferts sont de nouveau possibles.
        _factory.Time.Advance(TimeSpan.FromDays(300));
        var license = await GetLicenseAsync(key);
        license.TransfersRemaining.Should().Be(1);
        license.Activations.Count(a => a.DeactivatedAt is null).Should().Be(1);
    }

    [Fact]
    public async Task Transfert_depuis_le_pc_deja_actif_ne_compte_pas()
    {
        var (_, key) = await CreateLicenseAsync();
        await _factory.ClientFor(Pc1).ActivateAsync(key, default);

        var result = await _factory.ClientFor(Pc1).TransferAsync(key, default);

        result.Success.Should().BeTrue();
        result.TransfersRemaining.Should().Be(2);
    }

    [Fact]
    public async Task Hors_ligne_14_jours_puis_retour()
    {
        var (_, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);
        await pc1.ActivateAsync(key, default);

        _factory.Time.Advance(TimeSpan.FromDays(15));
        pc1.GetStatus().State.Should().Be(LicenseState.OfflineTooLong);

        (await pc1.RevalidateAsync(default)).Success.Should().BeTrue();
        pc1.GetStatus().IsPremium.Should().BeTrue();
    }

    [Fact]
    public async Task Revocation_retour_a_l_offre_gratuite()
    {
        var (id, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);
        await pc1.ActivateAsync(key, default);

        using (var admin = _factory.Admin())
        {
            (await admin.PostAsJsonAsync($"/admin/api/licenses/{id}/revoke", new RevokeRequest("fraude"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        _factory.Time.Advance(TimeSpan.FromDays(7));
        var result = await pc1.RevalidateAsync(default);

        result.Reason.Should().Be(FailureReason.LicenseRevoked);
        pc1.GetStatus().State.Should().Be(LicenseState.Revoked);
        (await _factory.ClientFor(Pc2).ActivateAsync(key, default)).Reason.Should().Be(FailureReason.LicenseRevoked);
    }

    [Fact]
    public async Task Licence_expiree_refusee()
    {
        var (_, key) = await CreateLicenseAsync(validDays: 30);
        _factory.Time.Advance(TimeSpan.FromDays(31));

        (await _factory.ClientFor(Pc1).ActivateAsync(key, default)).Reason.Should().Be(FailureReason.LicenseExpired);
    }

    [Fact]
    public async Task Offre_famille_trois_postes()
    {
        var (_, key) = await CreateLicenseAsync(LicenseTier.Family);

        (await _factory.ClientFor(Pc1).ActivateAsync(key, default)).Success.Should().BeTrue();
        (await _factory.ClientFor(Pc2).ActivateAsync(key, default)).Success.Should().BeTrue();
        (await _factory.ClientFor(Pc3).ActivateAsync(key, default)).Success.Should().BeTrue();
        var fourth = await _factory.ClientFor(new HardwareIdentity("B4", "D4", "C4", "G4")).ActivateAsync(key, default);

        fourth.Reason.Should().Be(FailureReason.LicenseUsedElsewhere);
        (await GetLicenseAsync(key)).Seats.Should().Be(3);
    }

    [Fact]
    public async Task Desactivation_libere_la_place()
    {
        var (_, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);
        await pc1.ActivateAsync(key, default);

        (await pc1.DeactivateAsync(default)).Success.Should().BeTrue();

        (await _factory.ClientFor(Pc2).ActivateAsync(key, default)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Cle_inconnue_refusee_et_journalisee()
    {
        var result = await _factory.ClientFor(Pc1).ActivateAsync(LicenseKeyFormat.Generate(), default);

        result.Reason.Should().Be(FailureReason.LicenseInvalidKey);
    }

    [Fact]
    public async Task Requetes_invalides_refusees()
    {
        using var client = _factory.CreateClient();
        var fp = HardwareFingerprint.Compute(Pc1).Components;

        async Task<LicenseErrorCode> Post(string path, object body)
        {
            var response = await client.PostAsJsonAsync(path, body, PcSanteJson.Options);
            return (await response.Content.ReadFromJsonAsync<LicenseServerResponse>(PcSanteJson.Options))!.Error;
        }

        (await Post(LicenseProtocol.ActivatePath, new ActivationRequest("x", fp, null))).Should().Be(LicenseErrorCode.InvalidRequest);
        (await Post(LicenseProtocol.ActivatePath, new ActivationRequest(LicenseKeyFormat.Generate(), ["", "", "", ""], null))).Should().Be(LicenseErrorCode.InvalidRequest);
        (await Post(LicenseProtocol.TransferPath, new TransferRequest("x", fp))).Should().Be(LicenseErrorCode.InvalidRequest);
        (await Post(LicenseProtocol.RevalidatePath, new RevalidationRequest("zz", Guid.NewGuid(), fp))).Should().Be(LicenseErrorCode.InvalidRequest);
        (await Post(LicenseProtocol.RevalidatePath, new RevalidationRequest(new string('A', 64), Guid.NewGuid(), fp))).Should().Be(LicenseErrorCode.InvalidKey);
        (await Post(LicenseProtocol.DeactivatePath, new DeactivationRequest("zz", Guid.NewGuid(), fp))).Should().Be(LicenseErrorCode.InvalidRequest);
        (await Post(LicenseProtocol.DeactivatePath, new DeactivationRequest(new string('A', 64), Guid.NewGuid(), fp))).Should().Be(LicenseErrorCode.InvalidKey);
    }

    [Fact]
    public async Task Revalidation_depuis_un_autre_materiel_refusee()
    {
        var (_, key) = await CreateLicenseAsync();
        var pc1 = _factory.ClientFor(Pc1);
        var result = await pc1.ActivateAsync(key, default);
        using var client = _factory.CreateClient();
        var token = LicenseTokenCodec.Verify(ReadToken(pc1), Convert.FromBase64String(_factory.PublicKeyBase64))!;

        var response = await client.PostAsJsonAsync(LicenseProtocol.RevalidatePath,
            new RevalidationRequest(token.KeyHash, token.ActivationId, HardwareFingerprint.Compute(Pc2).Components), PcSanteJson.Options);
        var body = await response.Content.ReadFromJsonAsync<LicenseServerResponse>(PcSanteJson.Options);
        body!.Error.Should().Be(LicenseErrorCode.AlreadyUsedElsewhere);

        var deactivate = await client.PostAsJsonAsync(LicenseProtocol.DeactivatePath,
            new DeactivationRequest(token.KeyHash, token.ActivationId, HardwareFingerprint.Compute(Pc2).Components), PcSanteJson.Options);
        (await deactivate.Content.ReadFromJsonAsync<LicenseServerResponse>(PcSanteJson.Options))!.Error.Should().Be(LicenseErrorCode.NotActivated);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Administration_protegee_par_cle()
    {
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/admin/api/licenses")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        anonymous.DefaultRequestHeaders.Add("X-Admin-Key", "mauvaise-cle");
        (await anonymous.GetAsync("/admin/api/refused")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var admin = _factory.Admin();
        (await admin.GetAsync("/admin/api/licenses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"/admin/api/licenses/{Guid.NewGuid()}/revoke", JsonContent.Create(new RevokeRequest(null)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/admin/api/licenses/{Guid.NewGuid()}/reset-transfers", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/admin/api/licenses", new GenerateKeysRequest(0, LicenseTier.Premium, null, null, null), PcSanteJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/admin/api/licenses", new GenerateKeysRequest(1, LicenseTier.Free, null, null, null), PcSanteJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Page_d_administration_et_sante()
    {
        using var client = _factory.CreateClient();

        (await client.GetStringAsync("/admin/index.html")).Should().Contain("Administration des licences");
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cles_generees_affichees_une_fois_stockees_hachees()
    {
        var (_, key) = await CreateLicenseAsync();
        var license = await GetLicenseAsync(key);

        JsonSerializer.Serialize(license, PcSanteJson.Options).Should().NotContain(key[4..]);
    }

    [Fact]
    public async Task Manifeste_de_mise_a_jour_absent_puis_signe()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync(LicenseProtocol.UpdatePath)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await new HttpLicenseServerClient(client).GetLatestUpdateAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task Manifeste_de_mise_a_jour_signe_ed25519()
    {
        using var factory = new LicenseServerFactory();
        factory.ExtraSettings["Updates:Version"] = "1.1.0";
        factory.ExtraSettings["Updates:DownloadUrl"] = "https://example.invalid/PcSante-1.1.0.msi";
        factory.ExtraSettings["Updates:Sha256"] = new string('a', 64);

        var manifest = await new HttpLicenseServerClient(factory.CreateClient()).GetLatestUpdateAsync(default);

        manifest.Should().NotBeNull();
        Ed25519.Verify(Convert.FromBase64String(factory.PublicKeyBase64), Encoding.UTF8.GetBytes(manifest!.Manifest), Convert.FromBase64String(manifest.Signature))
            .Should().BeTrue();
        JsonSerializer.Deserialize<UpdateManifest>(manifest.Manifest, PcSanteJson.Options)!.Version.Should().Be("1.1.0");
    }

    [Fact]
    public async Task Limitation_du_nombre_de_requetes_par_ip()
    {
        using var factory = new LicenseServerFactory(publicPerMinute: 3);
        var manager = factory.ClientFor(Pc1);
        var key = LicenseKeyFormat.Generate();

        var results = new List<LicenseOperationResult>();
        for (var i = 0; i < 5; i++)
        {
            results.Add(await manager.ActivateAsync(key, default));
        }

        results.Last().MessageKey.Should().Be("License_RateLimited");
    }

    [Fact]
    public void Commande_keygen_sans_ecriture_disque()
    {
        using var writer = new StringWriter();

        LicenseServerApp.PrintNewKeys(writer);

        var text = writer.ToString();
        text.Should().Contain(Services.ServerKeys.PrivateKeyVariable).And.Contain(Services.ServerKeys.AdminKeyVariable);
    }

    [Fact]
    public void Cles_du_serveur_obligatoires()
    {
        var missing = () => Services.ServerKeys.FromEnvironment(_ => null);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*keygen*");

        var notBase64 = () => Services.ServerKeys.FromEnvironment(n => n == Services.ServerKeys.PrivateKeyVariable ? "!!!" : "x");
        notBase64.Should().Throw<InvalidOperationException>();

        var shortAdmin = () => Services.ServerKeys.FromEnvironment(n => n == Services.ServerKeys.PrivateKeyVariable ? Convert.ToBase64String(new byte[32]) : "court");
        shortAdmin.Should().Throw<InvalidOperationException>();

        var badKey = () => new Services.ServerKeys(new byte[3], new string('x', 40));
        badKey.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Comparaison_de_cle_admin_en_temps_constant()
    {
        AdminApi.FixedTimeEquals("abc", "abc").Should().BeTrue();
        AdminApi.FixedTimeEquals("abd", "abc").Should().BeFalse();
    }

    private static string ReadToken(LicenseManager manager)
    {
        var field = typeof(LicenseManager).GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((ILicenseStateStore)field.GetValue(manager)!).Load().Token!;
    }

    private async Task<LicenseView> GetLicenseAsync(string key)
    {
        using var admin = _factory.Admin();
        var list = await admin.GetFromJsonAsync<List<LicenseView>>($"/admin/api/licenses?hint={key[^4..]}", PcSanteJson.Options);
        return list!.Single();
    }

    public void Dispose() => _factory.Dispose();
}
