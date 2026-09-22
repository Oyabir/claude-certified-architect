using PcSante.Core.Licensing;

namespace PcSante.Licensing.Tests;

public class HardwareFingerprintTests
{
    [Fact]
    public void Quatre_hachages_sha256_sans_donnee_brute()
    {
        var fp = HardwareFingerprint.Compute(FakeHardware.Pc1);

        fp.Components.Should().HaveCount(4).And.OnlyContain(c => c.Length == 64);
        string.Join("", fp.Components).Should().NotContain("BOARD").And.NotContain("111");
        HardwareFingerprint.IsWellFormed(fp.Components).Should().BeTrue();
    }

    [Fact]
    public void Meme_materiel_meme_empreinte_insensible_casse_espaces()
    {
        var a = HardwareFingerprint.Compute(FakeHardware.Pc1);
        var b = HardwareFingerprint.Compute(new HardwareIdentity(" board-111 ", "DISK 111", "cpu-111", "GUID-111"));

        a.CountMatches(b).Should().Be(3, "DISK 111 et DISK-111 diffèrent");
        a.Should().Be(HardwareFingerprint.Compute(FakeHardware.Pc1));
        a.GetHashCode().Should().Be(HardwareFingerprint.Compute(FakeHardware.Pc1).GetHashCode());
    }

    [Fact]
    public void Tolerance_trois_sur_quatre()
    {
        var original = HardwareFingerprint.Compute(FakeHardware.Pc1);
        var newDisk = HardwareFingerprint.Compute(FakeHardware.Pc1 with { SystemDiskSerial = "NEW-DISK" });
        var twoChanged = HardwareFingerprint.Compute(FakeHardware.Pc1 with { SystemDiskSerial = "X", MotherboardId = "Y" });

        original.Matches(newDisk).Should().BeTrue("changer un disque ne doit pas bloquer le client");
        original.Matches(twoChanged).Should().BeFalse();
        original.Matches(HardwareFingerprint.Compute(FakeHardware.Pc2)).Should().BeFalse();
    }

    [Fact]
    public void Valeurs_generiques_ou_vides_ne_comptent_pas()
    {
        var generic = HardwareFingerprint.Compute(new HardwareIdentity("To be filled by O.E.M.", null, "CPU-111", "guid-111"));

        generic.Components[0].Should().BeEmpty();
        generic.Components[1].Should().BeEmpty();
        generic.CountMatches(generic).Should().Be(2);
        generic.Matches(generic).Should().BeFalse("deux éléments inconnus ne suffisent pas à identifier le PC");
    }

    [Fact]
    public void Formats_invalides()
    {
        HardwareFingerprint.IsWellFormed(null).Should().BeFalse();
        HardwareFingerprint.IsWellFormed(["a", "b", "c"]).Should().BeFalse();
        HardwareFingerprint.IsWellFormed(["zz", "", "", ""]).Should().BeFalse();
        var act = () => new HardwareFingerprint(["a"]);
        act.Should().Throw<ArgumentException>();
    }
}

public class LicenseTokenTests
{
    private static readonly (byte[] Private, byte[] Public) Keys = Ed25519.GenerateKeyPair();

    private static LicenseTokenPayload Payload => new()
    {
        KeyHash = new string('A', 64),
        KeyHint = "…ABCD",
        ActivationId = Guid.NewGuid(),
        Fingerprint = HardwareFingerprint.Compute(FakeHardware.Pc1).Components,
        Tier = LicenseTier.Premium,
        IssuedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(14),
    };

    [Fact]
    public void Jeton_signe_verifie()
    {
        var payload = Payload;
        var token = LicenseTokenCodec.Sign(payload, Keys.Private);

        var verified = LicenseTokenCodec.Verify(token, Keys.Public);

        verified.Should().NotBeNull();
        verified!.ActivationId.Should().Be(payload.ActivationId);
        verified.Tier.Should().Be(LicenseTier.Premium);
        token.Should().NotContain("=").And.NotContain("+").And.NotContain("/");
    }

    [Fact]
    public void Jeton_modifie_refuse()
    {
        var token = LicenseTokenCodec.Sign(Payload, Keys.Private);
        var parts = token.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[0])).Replace("Premium", "Family", StringComparison.Ordinal);
        var forged = Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json)) + "." + parts[1];

        LicenseTokenCodec.Verify(forged, Keys.Public).Should().BeNull();
    }

    [Fact]
    public void Jeton_signe_par_une_autre_cle_refuse()
    {
        var other = Ed25519.GenerateKeyPair();

        LicenseTokenCodec.Verify(LicenseTokenCodec.Sign(Payload, other.PrivateKey), Keys.Public).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    [InlineData("eyJ9.AAAA")]
    public void Jetons_mal_formes_refuses(string? token)
    {
        LicenseTokenCodec.Verify(token, Keys.Public).Should().BeNull();
    }

    [Fact]
    public void Ed25519_rejette_les_entrees_invalides()
    {
        Ed25519.Verify([1, 2], [1], new byte[64]).Should().BeFalse();
        Ed25519.Verify(Keys.Public, [1], new byte[10]).Should().BeFalse();
        var act = () => Ed25519.Sign([1, 2, 3], [1]);
        act.Should().Throw<ArgumentException>();
        Ed25519.PublicKeyFromPrivate(Keys.Private).Should().Equal(Keys.Public);
    }

    [Fact]
    public void Base64url_longueurs()
    {
        foreach (var n in new[] { 1, 2, 3, 4, 5 })
        {
            var data = Enumerable.Range(0, n).Select(i => (byte)(i * 70)).ToArray();
            Base64Url.DecodeFromChars(Base64Url.EncodeToString(data)).Should().Equal(data);
        }

        var act = () => Base64Url.DecodeFromChars("A");
        act.Should().Throw<FormatException>();
    }
}
