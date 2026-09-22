using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Licensing;
using PcSante.Core.Processes;
using PcSante.Core.Scheduling;
using PcSante.Core.Settings;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.Core.Tests;

public class LicenseKeyFormatTests
{
    [Fact]
    public void Cle_generee_bien_formee_et_unique()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => LicenseKeyFormat.Generate()).ToList();

        keys.Should().OnlyHaveUniqueItems();
        keys.Should().OnlyContain(k => LicenseKeyFormat.IsWellFormed(k) && k.StartsWith("PCS-", StringComparison.Ordinal) && k.Length == 27);
    }

    [Fact]
    public void Saisie_tolerante_et_normalisee()
    {
        var key = LicenseKeyFormat.Generate();
        var typed = " " + key.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant() + " ";

        LicenseKeyFormat.Normalize(typed).Should().Be(key);
        LicenseKeyFormat.Normalize(key[4..]).Should().Be(key);
    }

    [Fact]
    public void Confusions_O_0_et_I_1_corrigees()
    {
        var key = "PCS-0000000000000000000";
        var body = key[4..];
        var sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += (i + 1) * LicenseKeyFormat.Alphabet.IndexOf(body[i], StringComparison.Ordinal);
        }

        var valid = "PCS-00000-00000-00000-0000" + LicenseKeyFormat.Alphabet[sum % 32];
        LicenseKeyFormat.Normalize(valid.Replace('0', 'O')).Should().Be(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PCS-12345")]
    [InlineData("PCS-UUUUU-UUUUU-UUUUU-UUUUU")]
    [InlineData("PCS-ABCDE-ABCDE-ABCDE-ABCDE-EXTRA")]
    public void Cles_invalides(string? input)
    {
        LicenseKeyFormat.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void Faute_de_frappe_detectee_par_la_somme_de_controle()
    {
        var key = LicenseKeyFormat.Generate();
        var c = key[5];
        var other = LicenseKeyFormat.Alphabet[(LicenseKeyFormat.Alphabet.IndexOf(c, StringComparison.Ordinal) + 1) % 32];
        var typo = key[..5] + other + key[6..];

        LicenseKeyFormat.IsWellFormed(typo).Should().BeFalse();
    }

    [Fact]
    public void Hachage_et_indice()
    {
        var key = LicenseKeyFormat.Generate();

        LicenseKeyFormat.Hash(key).Should().HaveLength(64);
        LicenseKeyFormat.Hint(key).Should().Be("…" + key[^4..]);
    }

    [Fact]
    public void Statut_gratuit()
    {
        var s = LicenseStatus.Free(LicenseState.Revoked);
        s.IsPremium.Should().BeFalse();
        (s with { EffectiveTier = LicenseTier.Family }).IsPremium.Should().BeTrue();
    }
}

public class ProcessReputationTests
{
    private static readonly SignatureInfo MicrosoftSigned = new(true, true, "Microsoft Windows", "AB");
    private static readonly SignatureInfo ThirdPartySigned = new(true, true, "Contoso", "CD");

    [Theory]
    [InlineData("lsass.exe")]
    [InlineData("csrss")]
    [InlineData("PcSante.Service")]
    public void Processus_proteges(string name)
    {
        ProcessReputation.IsProtected(name).Should().BeTrue();
    }

    [Fact]
    public void Processus_ordinaire_non_protege()
    {
        ProcessReputation.IsProtected("chrome.exe").Should().BeFalse();
    }

    [Fact]
    public void Nom_systeme_hors_system32_suspect()
    {
        ProcessReputation.Classify("svchost.exe", @"C:\Users\a\AppData\Roaming\svchost.exe", MicrosoftSigned)
            .Should().Be(Reputation.Suspicious);
    }

    [Fact]
    public void Non_signe_dans_temp_suspect()
    {
        ProcessReputation.Classify("x", @"C:\Users\a\AppData\Local\Temp\x.exe", SignatureInfo.Unsigned).Should().Be(Reputation.Suspicious);
        ProcessReputation.Classify("x", @"C:\Users\a\Downloads\x.exe", SignatureInfo.Unsigned).Should().Be(Reputation.Suspicious);
    }

    [Fact]
    public void Classements()
    {
        ProcessReputation.Classify("svchost", @"C:\Windows\System32\svchost.exe", MicrosoftSigned).Should().Be(Reputation.Useful);
        ProcessReputation.Classify("jusched", @"C:\Program Files\Java\jusched.exe", ThirdPartySigned).Should().Be(Reputation.Unnecessary);
        ProcessReputation.Classify("app", @"C:\Program Files\App\app.exe", ThirdPartySigned).Should().Be(Reputation.Useful);
        ProcessReputation.Classify("app", @"C:\Program Files\App\app.exe", SignatureInfo.Unsigned).Should().Be(Reputation.Unknown);
        ProcessReputation.Classify("app", null, SignatureInfo.Unsigned).Should().Be(Reputation.Unknown);
    }
}

public class ScreenCatalogTests
{
    [Fact]
    public void Mode_simple_par_defaut_quatre_ecrans()
    {
        new UserSettings().Mode.Should().Be(DisplayMode.Simple);
        ScreenCatalog.VisibleScreens(DisplayMode.Simple)
            .Should().Equal(ScreenId.Home, ScreenId.Protection, ScreenId.Optimization, ScreenId.Reports);
    }

    [Fact]
    public void Mode_avance_tous_les_ecrans_du_MVP()
    {
        var screens = ScreenCatalog.VisibleScreens(DisplayMode.Advanced);

        screens.Should().Contain([ScreenId.Performance, ScreenId.Processes, ScreenId.System, ScreenId.Scheduling]);
        screens.Should().NotContain(ScreenId.Sessions, "les sessions sont prévues en V2");
    }

    [Fact]
    public void Disponibilites()
    {
        ScreenCatalog.GetAvailability(ScreenId.Processes, DisplayMode.Simple).Should().Be(ScreenAvailability.HiddenInSimpleMode);
        ScreenCatalog.GetAvailability(ScreenId.Sessions, DisplayMode.Advanced).Should().Be(ScreenAvailability.NotInThisVersion);
        ScreenCatalog.GetAvailability(ScreenId.Settings, DisplayMode.Simple).Should().Be(ScreenAvailability.Visible);
        ScreenCatalog.FooterScreens.Should().Contain([ScreenId.Settings, ScreenId.License]);
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcsante-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Valeurs_par_defaut_si_fichier_absent_ou_corrompu()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Load().Should().Be(new UserSettings());

        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.FilePath, "{ pas du json");
        store.Load().Language.Should().Be("fr");
    }

    [Fact]
    public void Aller_retour_et_normalisation()
    {
        var store = new SettingsStore(Path.Combine(_dir, "sub", "settings.json"));
        store.Save(new UserSettings { Language = "ar", Mode = DisplayMode.Advanced, Overlay = new OverlaySettings { FontSize = 8, OpacityPercent = 5, Enabled = true } });

        var loaded = store.Load();
        loaded.Language.Should().Be("ar");
        loaded.Mode.Should().Be(DisplayMode.Advanced);
        loaded.Overlay.FontSize.Should().Be(14, "taille minimale 14 px");
        loaded.Overlay.OpacityPercent.Should().Be(30);
        loaded.Overlay.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Langue_inconnue_ramenee_au_francais()
    {
        new UserSettings { Language = "de" }.Normalized().Language.Should().Be("fr");
        SettingsStore.DefaultPath().Should().EndWith("settings.json");
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

public class ScheduledTemplatesTests
{
    [Fact]
    public void Trois_modeles_MVP()
    {
        ScheduledTemplates.All.Select(t => t.Id).Should().Equal(
            ScheduledTemplateId.DailyAntivirusScan, ScheduledTemplateId.WeeklyCleanup, ScheduledTemplateId.WeeklyRestorePoint);
        ScheduledTemplates.Get(ScheduledTemplateId.DailyAntivirusScan).Default.IsDaily.Should().BeTrue();
        ScheduledTemplates.Get(ScheduledTemplateId.WeeklyCleanup).Commands.Should().Contain(CommandId.CleanTemporaryFiles);
    }

    [Fact]
    public void Commandes_des_modeles_dans_le_catalogue()
    {
        ScheduledTemplates.All.SelectMany(t => t.Commands).Should().OnlyContain(c => CommandDefinitions.All.ContainsKey(c));
    }
}

public class AuditFormattingTests
{
    [Fact]
    public void Cle_de_licence_masquee_dans_l_audit()
    {
        var key = LicenseKeyFormat.Generate();
        var outcome = CommandValidator.Validate("ActivateLicense", new Dictionary<string, string> { ["key"] = key }, false, _ => true);

        var text = AuditFormatting.FormatParameters(outcome.Parameters!);

        text.Should().NotContain(key[4..9]);
        text.Should().Contain(key[^4..]);
        AuditFormatting.FormatParameters(CommandParameters.Empty).Should().BeNull();
    }
}

public class ProductInfoTests
{
    [Fact]
    public void Identite_issue_de_branding_props()
    {
        ProductInfo.Name.Should().Be("PC Santé");
        ProductInfo.TechnicalName.Should().Be("PcSante");
        ProductInfo.PipeName.Should().Be("PcSante.Service.v1");
        ProductInfo.ServiceName.Should().Be("PcSanteService");
        ProductInfo.Version.Should().Be("1.0.0");
        ProductInfo.LicensePublicKey.Should().BeEmpty("aucune clé de production n'est dans le dépôt");
    }
}

public class CommandResultTests
{
    [Fact]
    public void Donnees_serialisees_et_relues()
    {
        var result = CommandResult.WithData(new DriveSpace("C:", 100, 40));

        result.GetData<DriveSpace>()!.FreePercent.Should().Be(40);
        CommandResult.Success("k").GetData<DriveSpace>().Should().BeNull();
        CommandResult.Failure(FailureReason.ExecutionFailed, "k").IsSuccess.Should().BeFalse();
        new DriveSpace("C:", 0, 0).FreePercent.Should().Be(0);
    }
}

public class OverlayFormatterTests
{
    private static readonly OverlayLabels Labels = new("CPU", "RAM", "Disque", "Réseau", "Temp.", "Batterie", "n/d");
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void Seuls_les_indicateurs_choisis()
    {
        var m = new LiveMetrics { CpuPercent = 12.4, MemoryPercent = 55, DiskActivityPercent = 3 };

        OverlayFormatter.Format(m, OverlayIndicators.Cpu | OverlayIndicators.Memory, Labels, Fr).Should().Be("CPU 12 %   RAM 55 %");
    }

    [Fact]
    public void Tous_les_indicateurs()
    {
        var m = new LiveMetrics
        {
            CpuPercent = 5, MemoryPercent = 40, DiskActivityPercent = 1, NetworkReceivedBytesPerSecond = 2 * 1024 * 1024,
            NetworkSentBytesPerSecond = 500, CpuTemperatureCelsius = 51, BatteryPercent = 80, OnAcPower = true,
        };

        var text = OverlayFormatter.Format(m, OverlayIndicators.All, Labels, Fr, " | ");

        text.Should().Contain("Réseau ↓2,0 M ↑500 B").And.Contain("Temp. 51 °C").And.Contain("Batterie 80 % ⚡");
        OverlayFormatter.Format(m with { CpuTemperatureCelsius = null, BatteryPercent = null }, OverlayIndicators.Temperature | OverlayIndicators.Battery, Labels, Fr)
            .Should().Be("Temp. n/d");
        OverlayFormatter.Rate(4096, Fr).Should().Be("4 K");
    }
}
