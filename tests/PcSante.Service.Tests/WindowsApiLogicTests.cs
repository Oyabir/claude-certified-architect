using PcSante.Core.Scheduling;
using Microsoft.Win32.TaskScheduler;
using PcSante.Core.Windows;
using PcSante.WindowsApi;
using PcSante.WindowsApi.Native;

namespace PcSante.Service.Tests;

/// <summary>Logique pure des accès Windows (sans appel système) : exécutable sur toute plateforme.</summary>
public class WindowsApiLogicTests
{
    [Fact]
    public void Analyse_de_la_quarantaine_MpCmdRun()
    {
        const string output = """
            The following items are quarantined:

            ThreatName = Virus:DOS/EICAR_Test_File
                  file:C:\Users\alice\Downloads\eicar.com quarantined at 9/22/2026 10:15:02 (UTC)
            ThreatName = Trojan:Win32/Wacatac.B!ml
                  file:C:\Temp\x.exe quarantined at 9/21/2026 08:00:00 (UTC)
            """;

        var items = WindowsDefenderApi.ParseQuarantine(output);

        items.Should().HaveCount(2);
        items[0].Id.Should().Be("Virus:DOS/EICAR_Test_File");
        items[0].Path.Should().Be(@"C:\Users\alice\Downloads\eicar.com");
        items[0].QuarantinedAt.Should().Be(new DateTimeOffset(2026, 9, 22, 10, 15, 2, TimeSpan.Zero));
        WindowsDefenderApi.ParseQuarantine("No items in quarantine").Should().BeEmpty();
    }

    [Fact]
    public void Valeurs_StartupApproved_comme_le_gestionnaire_des_taches()
    {
        WindowsStartupApi.ApprovedValue(true)[0].Should().Be(0x02);
        var disabled = WindowsStartupApi.ApprovedValue(false);
        disabled[0].Should().Be(0x03);
        disabled.Should().HaveCount(12);
        BitConverter.ToInt64(disabled, 4).Should().BeGreaterThan(0, "date de désactivation enregistrée");
        WindowsStartupApi.IsApproved(null).Should().BeTrue("sans valeur, le programme démarre");
        WindowsStartupApi.IsApproved([0x06, 0, 0]).Should().BeTrue();
        WindowsStartupApi.IsApproved([0x03, 0, 0]).Should().BeFalse();
    }

    [Theory]
    [InlineData(0u, 0u, BitLockerState.Off)]
    [InlineData(1u, 1u, BitLockerState.On)]
    [InlineData(1u, 0u, BitLockerState.Paused)]
    [InlineData(2u, 0u, BitLockerState.Encrypting)]
    [InlineData(3u, 1u, BitLockerState.Decrypting)]
    [InlineData(4u, 0u, BitLockerState.Paused)]
    [InlineData(9u, 0u, BitLockerState.Unknown)]
    public void Etat_bitlocker(uint conversion, uint protection, BitLockerState expected)
    {
        WindowsBitLockerApi.MapState(conversion, protection).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, SessionState.Locked)]
    [InlineData(0, 1, SessionState.Active)]
    [InlineData(4, 1, SessionState.Disconnected)]
    [InlineData(1, 1, SessionState.Other)]
    public void Etat_de_session(int wtsState, int flags, SessionState expected)
    {
        WindowsSessionApi.MapState(wtsState, flags).Should().Be(expected);
    }

    [Fact]
    public void Adresse_du_client_bureau_a_distance()
    {
        WindowsSessionApi.ToAddress(new PcSante.WindowsApi.Native.Wts.ClientAddress { AddressFamily = 2, Address = [0, 0, 192, 168, 1, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] })
            .Should().Be("192.168.1.20");
        WindowsSessionApi.ToAddress(new PcSante.WindowsApi.Native.Wts.ClientAddress { AddressFamily = 2, Address = new byte[20] }).Should().BeNull();
        WindowsSessionApi.ToAddress(new PcSante.WindowsApi.Native.Wts.ClientAddress { AddressFamily = 23, Address = new byte[20] }).Should().BeNull();
    }

    [Theory]
    [InlineData((ushort)3, DiskMediaType.Hdd)]
    [InlineData((ushort)4, DiskMediaType.Ssd)]
    [InlineData((ushort)0, DiskMediaType.Unknown)]
    public void Type_de_disque(ushort mediaType, DiskMediaType expected)
    {
        WindowsDiskOptimizationApi.MapMediaType(mediaType).Should().Be(expected);
    }

    [Fact]
    public void Arguments_de_tache_avec_langue_du_rapport()
    {
        WindowsScheduledTemplateApi.Arguments(ScheduledTemplateId.WeeklyCleanup, null).Should().Be("--run-task WeeklyCleanup");
        var withLanguage = WindowsScheduledTemplateApi.Arguments(ScheduledTemplateId.MonthlyReport, "ar");
        withLanguage.Should().Be("--run-task MonthlyReport --lang ar");
        WindowsScheduledTemplateApi.LanguageOf(withLanguage).Should().Be("ar");
        WindowsScheduledTemplateApi.LanguageOf("--run-task WeeklyCleanup").Should().BeNull();
        WindowsScheduledTemplateApi.LanguageOf(null).Should().BeNull();
    }

    [Theory]
    [InlineData(0x061100u, true)]
    [InlineData(0x041000u, true)]
    [InlineData(0x060100u, false)]
    [InlineData(0x062000u, false)]
    [InlineData(0u, false)]
    public void Etat_d_un_antivirus_du_centre_de_securite(uint productState, bool enabled)
    {
        WindowsDefenderApi.IsProductEnabled(productState).Should().Be(enabled);
    }

    [Theory]
    [InlineData(0x061100u, true)]
    [InlineData(0x061110u, false)]
    [InlineData(0x041000u, true)]
    public void Signatures_a_jour_selon_le_centre_de_securite(uint productState, bool upToDate)
    {
        WindowsSecurityCenterApi.IsSignatureUpToDate(productState).Should().Be(upToDate);
    }

    [Fact]
    public void Defender_exclu_des_antivirus_tiers()
    {
        WindowsDefenderApi.IsDefenderProduct("windowsdefender://").Should().BeTrue();
        WindowsDefenderApi.IsDefenderProduct(@"C:\Program Files\Norton\wsc_proxy.exe").Should().BeFalse();
        WindowsDefenderApi.IsDefenderProduct(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("MicrosoftEdgeAutoLaunch_507B6EAB61E0461F18F13A409140A5A9", "Microsoft Edge", "Microsoft Edge")]
    [InlineData("OneDrive", "  ", "OneDrive")]
    [InlineData("Outil", null, "Outil")]
    public void Nom_affiche_d_un_programme_au_demarrage(string registryName, string? description, string expected)
    {
        WindowsStartupApi.DisplayName(registryName, description).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\sync.exe -minimized", @"C:\Tools\sync.exe")]
    [InlineData("rundll32 shell32.dll,Control_RunDLL", null)]
    [InlineData("\"", null)]
    public void Executable_d_une_ligne_de_commande(string command, string? expected)
    {
        WindowsStartupApi.ExtractExecutable(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("Windows Resource Protection did not find any integrity violations.", 0, RepairOutcome.NoProblemFound)]
    [InlineData("La protection des ressources Windows n'a trouvé aucune violation d'intégrité.", 0, RepairOutcome.NoProblemFound)]
    [InlineData("Windows Resource Protection found corrupt files and successfully repaired them.", 0, RepairOutcome.Repaired)]
    [InlineData("Windows Resource Protection found corrupt files but was unable to fix some of them.", 0, RepairOutcome.ProblemsRemain)]
    [InlineData("", 0, RepairOutcome.NoProblemFound)]
    [InlineData("Accès refusé", 5, RepairOutcome.Failed)]
    public void Interpretation_de_SFC(string output, int exitCode, RepairOutcome expected)
    {
        WindowsSystemRepairApi.InterpretSfc(new ToolResult(exitCode, output)).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Temp\x.tmp", @"C:\Users\a\AppData\Local\Temp", true)]
    [InlineData(@"C:\Users\a\AppData\Local\Temp", @"C:\Users\a\AppData\Local\Temp", false)]
    [InlineData(@"C:\Windows\System32\kernel32.dll", @"C:\Users\a\AppData\Local\Temp", false)]
    [InlineData(@"C:\Users\a\AppData\Local\TempEvil\x", @"C:\Users\a\AppData\Local\Temp", false)]
    public void Suppression_limitee_a_la_racine_autorisee(string path, string root, bool allowed)
    {
        SecureDelete.IsUnder(path.Replace('\\', Path.DirectorySeparatorChar), root.Replace('\\', Path.DirectorySeparatorChar)).Should().Be(allowed);
    }

    [Fact]
    public void Chemin_final_normalise()
    {
        SecureDelete.NormalizeFinal(@"\\?\C:\Windows").Should().Be(@"C:\Windows");
        SecureDelete.NormalizeFinal(@"\\?\UNC\srv\share").Should().Be(@"\\srv\share");
        SecureDelete.NormalizeFinal(@"C:\x").Should().Be(@"C:\x");
    }

    [Fact]
    public void Taches_tierces_et_jours()
    {
        WindowsThirdPartyTaskApi.IsThirdParty(@"\Contoso\Updater").Should().BeTrue();
        WindowsThirdPartyTaskApi.IsThirdParty(@"\Microsoft\Windows\Defrag\ScheduledDefrag").Should().BeFalse();
        WindowsThirdPartyTaskApi.IsThirdParty(@"\PcSante\WeeklyCleanup").Should().BeFalse();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            WindowsScheduledTemplateApi.FromDaysOfWeek(WindowsScheduledTemplateApi.ToDaysOfWeek(day)).Should().Be(day);
        }

        WindowsScheduledTemplateApi.ToDaysOfWeek(DayOfWeek.Sunday).Should().Be(DaysOfTheWeek.Sunday);
        WindowsScheduledTemplateApi.FromDaysOfWeek(0).Should().Be(DayOfWeek.Sunday);
    }
}
