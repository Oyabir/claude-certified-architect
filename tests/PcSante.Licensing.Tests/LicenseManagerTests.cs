using PcSante.Core.Commands;
using PcSante.Core.Licensing;

namespace PcSante.Licensing.Tests;

public class LicenseManagerTests
{
    private readonly AdjustableTime _time = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly (byte[] Private, byte[] Public) _keys = Ed25519.GenerateKeyPair();
    private readonly FakeLicenseServer _server;
    private readonly InMemoryLicenseStateStore _store = new();
    private readonly FakeHardware _hardware = new(FakeHardware.Pc1);
    private readonly string _key = LicenseKeyFormat.Generate();

    public LicenseManagerTests() => _server = new FakeLicenseServer(_keys.Private, _time);

    private LicenseManager Manager(string? publicKey = null) =>
        new(_server, _store, _hardware, _time, publicKey ?? Convert.ToBase64String(_keys.Public));

    [Fact]
    public void Sans_cle_publique_offre_gratuite_non_configuree()
    {
        var manager = Manager(publicKey: "");
        manager.IsConfigured.Should().BeFalse();
        manager.GetStatus().State.Should().Be(LicenseState.NotConfigured);

        Manager(publicKey: "pas du base64!").IsConfigured.Should().BeFalse();
        Manager(publicKey: Convert.ToBase64String(new byte[5])).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Operations_impossibles_sans_configuration()
    {
        var manager = Manager(publicKey: "");

        (await manager.ActivateAsync(_key, default)).Reason.Should().Be(FailureReason.LicenseNotConfigured);
        (await manager.TransferAsync(_key, default)).Reason.Should().Be(FailureReason.LicenseNotConfigured);
        (await manager.RevalidateAsync(default)).Success.Should().BeFalse();
        (await manager.DeactivateAsync(default)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Activation_debloque_premium()
    {
        var manager = Manager();
        manager.GetStatus().State.Should().Be(LicenseState.NotActivated);

        var result = await manager.ActivateAsync(_key.ToLowerInvariant(), default);

        result.Success.Should().BeTrue();
        result.MessageKey.Should().Be("License_Activated");
        result.Status.State.Should().Be(LicenseState.Active);
        result.Status.EffectiveTier.Should().Be(LicenseTier.Premium);
        result.Status.IsPremium.Should().BeTrue();
        result.Status.KeyHint.Should().Be("…TEST");
        _store.Load().Token.Should().NotBeNull();
    }

    [Fact]
    public async Task Cle_mal_saisie_refusee_sans_appel_reseau()
    {
        var result = await Manager().ActivateAsync("PCS-XXXXX", default);

        result.Reason.Should().Be(FailureReason.LicenseInvalidKey);
        _server.Calls.Should().Be(0);
        (await Manager().TransferAsync("nope", default)).Reason.Should().Be(FailureReason.LicenseInvalidKey);
    }

    [Fact]
    public async Task Serveur_injoignable_message_clair()
    {
        _server.Online = false;

        var result = await Manager().ActivateAsync(_key, default);

        result.Reason.Should().Be(FailureReason.NetworkUnavailable);
        result.MessageKey.Should().Be("License_ServerUnreachable");
    }

    [Theory]
    [InlineData(LicenseErrorCode.AlreadyUsedElsewhere, FailureReason.LicenseUsedElsewhere, "License_UsedElsewhere")]
    [InlineData(LicenseErrorCode.InvalidKey, FailureReason.LicenseInvalidKey, "License_InvalidKey")]
    [InlineData(LicenseErrorCode.TransferLimitReached, FailureReason.LicenseTransferLimit, "License_TransferLimit")]
    [InlineData(LicenseErrorCode.Expired, FailureReason.LicenseExpired, "License_Expired")]
    [InlineData(LicenseErrorCode.RateLimited, FailureReason.NetworkUnavailable, "License_RateLimited")]
    [InlineData(LicenseErrorCode.ServerError, FailureReason.InternalError, "License_ServerError")]
    public async Task Refus_du_serveur_traduits(LicenseErrorCode error, FailureReason reason, string messageKey)
    {
        _server.ForcedError = error;

        var result = await Manager().ActivateAsync(_key, default);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(reason);
        result.MessageKey.Should().Be(messageKey);
        result.Status.IsPremium.Should().BeFalse();
    }

    [Fact]
    public async Task Jeton_falsifie_refuse()
    {
        _server.TamperToken = "abc.def";

        var result = await Manager().ActivateAsync(_key, default);

        result.MessageKey.Should().Be("License_InvalidServerAnswer");
        _store.Load().Token.Should().BeNull();
    }

    [Fact]
    public async Task Jeton_pour_un_autre_pc_refuse()
    {
        _server.ForcedFingerprint = HardwareFingerprint.Compute(FakeHardware.Pc2).Components;

        (await Manager().ActivateAsync(_key, default)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Hors_ligne_tolere_14_jours_puis_gratuit()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);
        _server.Online = false;

        _time.Advance(TimeSpan.FromDays(13));
        manager.GetStatus().IsPremium.Should().BeTrue();
        manager.IsRevalidationDue().Should().BeTrue("7 jours sont écoulés");
        (await manager.RevalidateAsync(default)).Reason.Should().Be(FailureReason.NetworkUnavailable);
        manager.GetStatus().IsPremium.Should().BeTrue("hors ligne ne change rien avant 14 jours");

        _time.Advance(TimeSpan.FromDays(2));
        var status = manager.GetStatus();
        status.State.Should().Be(LicenseState.OfflineTooLong);
        status.IsPremium.Should().BeFalse();

        _server.Online = true;
        (await manager.RevalidateAsync(default)).Success.Should().BeTrue();
        manager.GetStatus().IsPremium.Should().BeTrue("retour en ligne : Premium rétabli");
    }

    [Fact]
    public async Task Revalidation_seulement_apres_7_jours()
    {
        var manager = Manager();
        manager.IsRevalidationDue().Should().BeFalse("rien à revalider sans activation");
        await manager.ActivateAsync(_key, default);

        _time.Advance(TimeSpan.FromDays(6));
        manager.IsRevalidationDue().Should().BeFalse();
        _time.Advance(TimeSpan.FromDays(1));
        manager.IsRevalidationDue().Should().BeTrue();

        (await manager.RevalidateAsync(default)).MessageKey.Should().Be("License_Revalidated");
        manager.IsRevalidationDue().Should().BeFalse();
        manager.GetStatus().TokenExpiresAt.Should().Be(_time.GetUtcNow() + LicenseTokenCodec.TokenLifetime);
    }

    [Fact]
    public async Task Recul_de_l_horloge_bloque_le_jeton()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);
        _time.Advance(TimeSpan.FromDays(3));
        manager.GetStatus().IsPremium.Should().BeTrue();

        _time.SetUtcNow(_time.GetUtcNow().AddDays(-2));
        var status = manager.GetStatus();
        status.State.Should().Be(LicenseState.ClockTampered);
        status.IsPremium.Should().BeFalse();

        _time.Advance(TimeSpan.FromDays(3));
        manager.GetStatus().State.Should().Be(LicenseState.ClockTampered, "le blocage persiste jusqu'à une revalidation en ligne");
        manager.IsRevalidationDue().Should().BeTrue();

        (await manager.RevalidateAsync(default)).Success.Should().BeTrue();
        manager.GetStatus().State.Should().Be(LicenseState.Active);
    }

    [Fact]
    public async Task Petit_recul_d_horloge_tolere()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);
        _time.Advance(TimeSpan.FromHours(5));
        manager.GetStatus();

        _time.SetUtcNow(_time.GetUtcNow().AddHours(-1));

        manager.GetStatus().State.Should().Be(LicenseState.Active);
    }

    [Fact]
    public async Task Revocation_retour_a_gratuit()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);
        _time.Advance(TimeSpan.FromDays(8));
        _server.ForcedError = LicenseErrorCode.Revoked;

        var result = await manager.RevalidateAsync(default);

        result.Reason.Should().Be(FailureReason.LicenseRevoked);
        manager.GetStatus().State.Should().Be(LicenseState.Revoked);
        manager.GetStatus().IsPremium.Should().BeFalse();
        manager.IsRevalidationDue().Should().BeFalse();
        (await manager.RevalidateAsync(default)).Success.Should().BeFalse();

        _server.ForcedError = null;
        (await manager.ActivateAsync(_key, default)).Success.Should().BeTrue("une nouvelle activation réussie lève la révocation");
    }

    [Fact]
    public async Task Abonnement_expire_retour_a_gratuit()
    {
        _server.LicenseExpiresAt = _time.GetUtcNow().AddDays(3);
        var manager = Manager();
        await manager.ActivateAsync(_key, default);

        _time.Advance(TimeSpan.FromDays(4));

        manager.GetStatus().State.Should().Be(LicenseState.Expired);
    }

    [Fact]
    public async Task Copie_du_jeton_sur_un_autre_pc_inutilisable()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);

        _hardware.Identity = FakeHardware.Pc2;
        var onOtherPc = new LicenseManager(_server, _store, _hardware, _time, Convert.ToBase64String(_keys.Public));

        onOtherPc.GetStatus().State.Should().Be(LicenseState.WrongComputer);
        onOtherPc.GetStatus().IsPremium.Should().BeFalse();
    }

    [Fact]
    public async Task Jeton_stocke_corrompu()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);
        _store.Save(_store.Load() with { Token = "corrompu.x" });

        manager.GetStatus().State.Should().Be(LicenseState.InvalidToken);
    }

    [Fact]
    public async Task Transfert_et_desactivation()
    {
        var manager = Manager();
        var transfer = await manager.TransferAsync(_key, default);
        transfer.Success.Should().BeTrue();
        transfer.MessageKey.Should().Be("License_Transferred");
        transfer.TransfersRemaining.Should().Be(1);

        var deactivate = await manager.DeactivateAsync(default);
        deactivate.Success.Should().BeTrue();
        manager.GetStatus().State.Should().Be(LicenseState.NotActivated);
    }

    [Fact]
    public async Task Desactivation_hors_ligne_ou_refusee()
    {
        var manager = Manager();
        await manager.ActivateAsync(_key, default);

        _server.Online = false;
        (await manager.DeactivateAsync(default)).Reason.Should().Be(FailureReason.NetworkUnavailable);

        _server.Online = true;
        _server.ForcedError = LicenseErrorCode.ServerError;
        (await manager.DeactivateAsync(default)).Success.Should().BeFalse();
        manager.GetStatus().IsPremium.Should().BeTrue();

        _server.ForcedError = LicenseErrorCode.NotActivated;
        (await manager.DeactivateAsync(default)).Success.Should().BeTrue("déjà désactivé côté serveur");
    }

    [Fact]
    public async Task Offre_famille()
    {
        _server.Tier = LicenseTier.Family;

        (await Manager().ActivateAsync(_key, default)).Status.EffectiveTier.Should().Be(LicenseTier.Family);
    }

    [Fact]
    public void Traduction_des_codes()
    {
        LicenseManager.MapError(LicenseErrorCode.NotActivated).Should().Be(FailureReason.PreconditionFailed);
        LicenseManager.MessageFor(LicenseErrorCode.NotActivated).Should().Be("License_NotActivated");
        LicenseManager.MessageFor(LicenseErrorCode.Revoked).Should().Be("License_Revoked");
        LicenseProtocol.IsPaidTier(LicenseTier.Free).Should().BeFalse();
    }
}

public class LicenseStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcsante-lic-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Etat_chiffre_relu_sur_le_meme_pc()
    {
        var path = Path.Combine(_dir, "license.dat");
        var store = new ProtectedFileLicenseStateStore(path, new FakeMachineProtector(7));
        store.Load().Token.Should().BeNull();

        store.Save(new StoredLicenseState { Token = "abc.def", Revoked = true });

        store.Load().Token.Should().Be("abc.def");
        File.ReadAllText(path).Should().NotContain("abc.def", "le fichier est chiffré");
    }

    [Fact]
    public void Fichier_copie_sur_un_autre_pc_illisible()
    {
        var path = Path.Combine(_dir, "license.dat");
        new ProtectedFileLicenseStateStore(path, new FakeMachineProtector(7)).Save(new StoredLicenseState { Token = "abc.def" });

        new ProtectedFileLicenseStateStore(path, new FakeMachineProtector(9)).Load().Token.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }

        GC.SuppressFinalize(this);
    }
}
