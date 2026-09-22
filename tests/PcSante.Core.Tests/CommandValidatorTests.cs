using PcSante.Core.Commands;
using PcSante.Core.Licensing;

namespace PcSante.Core.Tests;

public class CommandValidatorTests
{
    private static readonly Func<string, bool> Exists = _ => true;

    private static ValidationOutcome Validate(string? command, Dictionary<string, string>? p = null, bool confirmed = false) =>
        CommandValidator.Validate(command, p, confirmed, Exists);

    [Theory]
    [InlineData("FormatDisk")]
    [InlineData("RunPowerShell")]
    [InlineData("runhealthanalysis")]
    [InlineData("100")]
    [InlineData("RunHealthAnalysis; del *")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void Commande_hors_catalogue_refusee(string? command)
    {
        var outcome = Validate(command);

        outcome.IsValid.Should().BeFalse();
        outcome.Reason.Should().Be(FailureReason.NotInCatalog);
    }

    [Fact]
    public void Commande_du_catalogue_sans_parametre_acceptee()
    {
        var outcome = Validate("RunHealthAnalysis");

        outcome.IsValid.Should().BeTrue();
        outcome.Descriptor!.Id.Should().Be(CommandId.RunHealthAnalysis);
        outcome.Parameters!.Values.Should().BeEmpty();
    }

    [Fact]
    public void Parametre_non_declare_refuse()
    {
        var outcome = Validate("RunHealthAnalysis", new() { ["script"] = "Get-Process" });

        outcome.IsValid.Should().BeFalse();
        outcome.Reason.Should().Be(FailureReason.InvalidParameters);
    }

    [Fact]
    public void Parametre_obligatoire_manquant_refuse()
    {
        Validate("EnableFirewallProfile").Reason.Should().Be(FailureReason.InvalidParameters);
    }

    [Theory]
    [InlineData("Public", true)]
    [InlineData("Private", true)]
    [InlineData("public", false)]
    [InlineData("All", false)]
    public void Choix_limite_a_la_liste_fermee(string profile, bool valid)
    {
        Validate("EnableFirewallProfile", new() { ["profile"] = profile }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Confirmation_obligatoire_pour_arreter_un_programme()
    {
        var p = new Dictionary<string, string> { ["pid"] = "1234" };

        Validate("StopProcess", p, confirmed: false).Reason.Should().Be(FailureReason.ConfirmationRequired);
        Validate("StopProcess", p, confirmed: true).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12a")]
    [InlineData("99999999999")]
    [InlineData(" 12")]
    public void Entier_invalide_refuse(string pid)
    {
        Validate("StopProcess", new() { ["pid"] = pid }, confirmed: true).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Users\Ali\Documents", true)]
    [InlineData(@"D:\", true)]
    [InlineData(@"\\serveur\partage", false)]
    [InlineData(@"Documents\x", false)]
    [InlineData(@"C:\Users\..\Windows", false)]
    [InlineData(@"C:\a*b", false)]
    [InlineData(@"C:\a:stream", false)]
    [InlineData(@"\\?\C:\x", false)]
    public void Chemin_local_securise(string path, bool valid)
    {
        CommandValidator.IsSafeLocalPath(path).Should().Be(valid);
    }

    [Fact]
    public void Chemin_inexistant_refuse()
    {
        CommandValidator.Validate("StartCustomScan", new Dictionary<string, string> { ["path"] = @"C:\absent" }, false, _ => false)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Cle_de_licence_validee_localement()
    {
        var key = LicenseKeyFormat.Generate();

        Validate("ActivateLicense", new() { ["key"] = key }).IsValid.Should().BeTrue();
        Validate("ActivateLicense", new() { ["key"] = "PCS-AAAAA-AAAAA-AAAAA-AAAAB" }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("08:30", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("8:30", false)]
    public void Heure_au_format_HH_mm(string time, bool valid)
    {
        Validate("EnableScheduledTemplate", new()
        {
            ["template"] = "WeeklyCleanup",
            ["day"] = "Sunday",
            ["time"] = time,
            ["onlyWhenIdle"] = "true",
            ["onlyOnAcPower"] = "false",
        }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("HKLM|Run|OneDrive", true)]
    [InlineData("..\\..\\x", false)]
    [InlineData("a\"b", false)]
    public void Identifiant_opaque(string id, bool valid)
    {
        Validate("DisableStartupItem", new() { ["id"] = id }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Valeur_trop_longue_refusee()
    {
        Validate("DisableStartupItem", new() { ["id"] = new string('a', 300) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Trop_de_parametres_refuses()
    {
        var p = Enumerable.Range(0, 20).ToDictionary(i => $"p{i}", i => "x");

        Validate("RunHealthAnalysis", p).Reason.Should().Be(FailureReason.InvalidParameters);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", false)]
    [InlineData("1", false)]
    public void Booleen_strict(string value, bool valid)
    {
        CommandValidator.IsValid(new ParameterSpec("b", ParameterType.Boolean), value, Exists).Should().Be(valid);
    }

    [Fact]
    public void Guid_strict()
    {
        CommandValidator.IsValid(new ParameterSpec("g", ParameterType.Guid), Guid.NewGuid().ToString(), Exists).Should().BeTrue();
        CommandValidator.IsValid(new ParameterSpec("g", ParameterType.Guid), "pas-un-guid", Exists).Should().BeFalse();
    }

    [Fact]
    public void Parametres_types_lisibles_apres_validation()
    {
        var outcome = Validate("EnableScheduledTemplate", new()
        {
            ["template"] = "DailyAntivirusScan",
            ["day"] = "Everyday",
            ["time"] = "12:30",
            ["onlyWhenIdle"] = "true",
            ["onlyOnAcPower"] = "false",
        });

        var p = outcome.Parameters!;
        p.GetTime("time").Should().Be(new TimeOnly(12, 30));
        p.GetBool("onlyWhenIdle").Should().BeTrue();
        p.GetEnum<Scheduling.ScheduledTemplateId>("template").Should().Be(Scheduling.ScheduledTemplateId.DailyAntivirusScan);
        p.GetOptionalString("absent").Should().BeNull();
        var settings = Scheduling.ScheduledTemplates.FromParameters(p);
        settings.IsDaily.Should().BeTrue();
        settings.OnlyOnAcPower.Should().BeFalse();
    }
}
