using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.Core.Tests;

public class DiagnosticRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static DefenderStatus HealthyDefender => new()
    {
        IsAvailable = true,
        IsActiveAntivirus = true,
        RealTimeProtectionEnabled = true,
        CloudProtectionEnabled = true,
        SignaturesUpdatedAt = Now.AddHours(-5),
        LastQuickScanAt = Now.AddDays(-1),
    };

    private static SystemSnapshot Healthy => new()
    {
        CollectedAt = Now,
        Defender = HealthyDefender,
        Firewall = [new(FirewallProfile.Domain, true), new(FirewallProfile.Private, true), new(FirewallProfile.Public, true)],
        Updates = new UpdateStatus(Now.AddDays(-1), Now.AddDays(-2), false, true),
        RestoreEnabled = true,
        LastRestorePointAt = Now.AddDays(-3),
        CrashesLast30Days = 0,
        EnabledStartupItems = 5,
        SystemDrive = new DriveSpace("C:", 500L << 30, 200L << 30),
        CleanableBytes = 100L << 20,
        MemoryPercent = 45,
    };

    [Fact]
    public void PC_sain_sans_aucun_probleme()
    {
        DiagnosticRules.EvaluateAll(Healthy).Should().BeEmpty();
    }

    [Fact]
    public void Donnees_inconnues_ne_creent_pas_de_faux_probleme()
    {
        DiagnosticRules.EvaluateAll(new SystemSnapshot { CollectedAt = Now }).Should().BeEmpty();
    }

    [Fact]
    public void Protection_temps_reel_desactivee_rouge_avec_correction()
    {
        var issues = DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { RealTimeProtectionEnabled = false } });

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("RealtimeOff");
        issue.Severity.Should().Be(IssueSeverity.Critical);
        issue.Fix!.Command.Should().Be(CommandId.EnableRealtimeProtection);
    }

    [Fact]
    public void Defender_passif_autre_antivirus_aucun_probleme()
    {
        var snapshot = Healthy with { Defender = HealthyDefender with { IsActiveAntivirus = false, RealTimeProtectionEnabled = false, CloudProtectionEnabled = false } };

        DiagnosticRules.EvaluateAll(snapshot).Should().BeEmpty();
    }

    [Fact]
    public void Aucun_antivirus_du_tout_probleme_rouge()
    {
        // Cas vu en test : Defender absent et aucun autre antivirus déclaré → Sécurité affichait 100.
        var none = Healthy with { Defender = DefenderStatus.Unavailable with { OtherActiveAntivirus = [] } };

        var issue = DiagnosticRules.EvaluateAll(none).Should().ContainSingle().Subject;
        issue.Code.Should().Be("NoAntivirus");
        issue.Severity.Should().Be(IssueSeverity.Critical);
        HealthScoreCalculator.BuildReport(Now, [issue], TimeSpan.Zero).SubScores.Security.Should().BeLessThan(HealthScoreCalculator.OrangeThreshold);
    }

    [Fact]
    public void Defender_absent_mais_autre_antivirus_ou_inconnu_aucun_probleme()
    {
        DiagnosticRules.EvaluateAll(Healthy with { Defender = DefenderStatus.Unavailable with { OtherActiveAntivirus = ["Norton 360"] } })
            .Should().BeEmpty("un autre antivirus protège le PC");
        DiagnosticRules.EvaluateAll(Healthy with { Defender = DefenderStatus.Unavailable })
            .Should().BeEmpty("centre de sécurité inconnu : aucune conclusion");
        DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { IsActiveAntivirus = false, OtherActiveAntivirus = ["Kaspersky"] } })
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(4, IssueSeverity.Warning)]
    [InlineData(8, IssueSeverity.Critical)]
    public void Anciennete_des_signatures(int days, IssueSeverity? expected)
    {
        var issues = DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { SignaturesUpdatedAt = Now.AddDays(-days) } });

        if (expected is null)
        {
            issues.Should().BeEmpty();
        }
        else
        {
            issues.Should().ContainSingle(i => i.Code == "SignaturesOutdated" && i.Severity == expected);
        }
    }

    [Fact]
    public void Aucun_scan_recent()
    {
        var issues = DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { LastQuickScanAt = null, LastFullScanAt = Now.AddDays(-30) } });

        issues.Should().ContainSingle(i => i.Code == "NoRecentScan" && i.Fix!.Command == CommandId.StartQuickScan);
    }

    [Fact]
    public void Scan_complet_recent_suffit()
    {
        DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { LastQuickScanAt = null, LastFullScanAt = Now.AddDays(-2) } })
            .Should().BeEmpty();
    }

    [Fact]
    public void Menaces_actives_et_protection_cloud()
    {
        var issues = DiagnosticRules.EvaluateAll(Healthy with { Defender = HealthyDefender with { ActiveThreats = 2, CloudProtectionEnabled = false } });

        issues.Should().Contain(i => i.Code == "ActiveThreats" && i.Args[0] == "2" && i.Fix!.Screen == ScreenId.Protection);
        issues.Should().Contain(i => i.Code == "CloudProtectionOff");
    }

    [Fact]
    public void Pare_feu_desactive_par_profil()
    {
        var issues = DiagnosticRules.EvaluateAll(Healthy with { Firewall = [new(FirewallProfile.Public, false), new(FirewallProfile.Private, true)] });

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("FirewallOffPublic");
        issue.Fix!.Parameters!["profile"].Should().Be("Public");
    }

    [Fact]
    public void Mises_a_jour_non_verifiees_ou_bloquees()
    {
        DiagnosticRules.EvaluateAll(Healthy with { Updates = new UpdateStatus(Now.AddDays(-20), null, false, true) })
            .Should().ContainSingle(i => i.Code == "UpdatesNotChecked");
        DiagnosticRules.EvaluateAll(Healthy with { Updates = new UpdateStatus(Now.AddDays(-20), null, false, false) })
            .Should().ContainSingle(i => i.Code == "UpdatesBroken" && i.Fix!.Command == CommandId.RepairWindowsUpdate);
        DiagnosticRules.EvaluateAll(Healthy with { Updates = new UpdateStatus(Now, null, true, true) })
            .Should().ContainSingle(i => i.Code == "RebootPending" && i.Severity == IssueSeverity.Info);
    }

    [Fact]
    public void Restauration_systeme()
    {
        DiagnosticRules.EvaluateAll(Healthy with { RestoreEnabled = false })
            .Should().ContainSingle(i => i.Code == "RestoreDisabled");
        DiagnosticRules.EvaluateAll(Healthy with { LastRestorePointAt = Now.AddDays(-45) })
            .Should().ContainSingle(i => i.Code == "NoRecentRestorePoint" && i.Severity == IssueSeverity.Info);
        DiagnosticRules.EvaluateAll(Healthy with { LastRestorePointAt = null })
            .Should().ContainSingle(i => i.Code == "NoRecentRestorePoint");
    }

    [Theory]
    [InlineData(1, IssueSeverity.Warning)]
    [InlineData(3, IssueSeverity.Critical)]
    public void Plantages_recents(int crashes, IssueSeverity severity)
    {
        DiagnosticRules.EvaluateAll(Healthy with { CrashesLast30Days = crashes })
            .Should().ContainSingle(i => i.Code == "RecentCrashes" && i.Severity == severity);
    }

    [Fact]
    public void Trop_de_programmes_au_demarrage()
    {
        DiagnosticRules.EvaluateAll(Healthy with { EnabledStartupItems = 20 })
            .Should().ContainSingle(i => i.Code == "ManyStartupPrograms" && i.Category == HealthCategory.Performance);
    }

    [Fact]
    public void Memoire_saturee()
    {
        DiagnosticRules.EvaluateAll(Healthy with { MemoryPercent = 95 })
            .Should().ContainSingle(i => i.Code == "HighMemory");
    }

    [Theory]
    [InlineData(10, IssueSeverity.Warning)]
    [InlineData(3, IssueSeverity.Critical)]
    public void Espace_disque(int freePercent, IssueSeverity severity)
    {
        var drive = new DriveSpace("C:", 100L << 30, freePercent * (1L << 30));

        var issue = DiagnosticRules.EvaluateAll(Healthy with { SystemDrive = drive }).Should().ContainSingle().Subject;
        issue.Code.Should().Be("LowDiskSpace");
        issue.Severity.Should().Be(severity);
        issue.Category.Should().Be(HealthCategory.Storage);
    }

    [Fact]
    public void Fichiers_nettoyables()
    {
        DiagnosticRules.EvaluateAll(Healthy with { CleanableBytes = 3L << 30 })
            .Should().ContainSingle(i => i.Code == "CleanableFiles" && i.Args[0] == "3");
    }
}
