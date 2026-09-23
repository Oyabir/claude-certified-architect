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
    public void Machine_virtuelle_a_deux_elements_lisibles_reconnue()
    {
        // Valeurs relevées dans Windows Sandbox : disque sans numéro, processeur « 0000000000000000 ».
        var sandbox = new HardwareIdentity("8733-1355-6204-0729-6041-9154-89|Virtual Machine", null, "0000000000000000", "37628ac8-eefa-4627-a066-95d75d46e045");
        var reference = HardwareFingerprint.Compute(sandbox);

        reference.ReadableCount.Should().Be(2);
        reference.Matches(HardwareFingerprint.Compute(sandbox)).Should().BeTrue("même machine, relue à l'identique");
        reference.Matches(HardwareFingerprint.Compute(sandbox with { MachineGuid = "autre" })).Should().BeFalse("les 2 éléments sont exigés");
    }

    [Fact]
    public void Masquer_ses_identifiants_ne_facilite_pas_la_correspondance()
    {
        var reference = HardwareFingerprint.Compute(FakeHardware.Pc1);
        var hidden = HardwareFingerprint.Compute(FakeHardware.Pc1 with { SystemDiskSerial = null, ProcessorId = "0000000000000000" });

        reference.Matches(hidden).Should().BeFalse("référence à 4 éléments : 3 correspondances exigées, seules 2 sont lisibles");
    }

    [Fact]
    public void Moins_de_deux_elements_lisibles_aucune_correspondance()
    {
        var single = HardwareFingerprint.Compute(new HardwareIdentity(null, null, "None", "guid-111"));

        single.ReadableCount.Should().Be(1);
        single.Matches(single).Should().BeFalse();
    }

    [Fact]
    public void Valeurs_generiques_ou_vides_ne_comptent_pas()
    {
        var generic = HardwareFingerprint.Compute(new HardwareIdentity("To be filled by O.E.M.", null, "CPU-111", "guid-111"));

        generic.Components[0].Should().BeEmpty();
        generic.Components[1].Should().BeEmpty();
        generic.CountMatches(generic).Should().Be(2);
        generic.Matches(generic).Should().BeTrue("les 2 éléments lisibles restants correspondent tous");
        generic.Matches(HardwareFingerprint.Compute(new HardwareIdentity("To be filled by O.E.M.", null, "CPU-222", "guid-111")))
            .Should().BeFalse("un des 2 éléments lisibles diffère : autre PC");
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
