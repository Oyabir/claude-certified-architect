using System.Globalization;
using FluentAssertions;
using PcSante.Core.Audit;
using PcSante.Core.Health;
using PcSante.Core.Reporting;
using PcSante.Core.Windows;
using Xunit;

namespace PcSante.Reporting.Tests;

public class ReportPdfBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static ReportData Sample(bool empty = false)
    {
        var issues = new List<HealthIssue>
        {
            new("RealtimeOff", HealthCategory.Security, IssueSeverity.Critical, null, []),
            new("LowDiskSpace", HealthCategory.Storage, IssueSeverity.Warning, null, ["12"]),
        };
        var health = HealthScoreCalculator.BuildReport(Now, issues, TimeSpan.FromSeconds(8));
        return new ReportData(
            ReportPeriod.Month, Now.AddMonths(-1), Now, "PC-SALON",
            empty ? null : health,
            empty ? [] : Enumerable.Range(0, 20).Select(i => new ScorePoint(Now.AddDays(-i), 40 + (i * 3), health.SubScores)).ToList(),
            empty ? [] : [new ThreatInfo("1", "Trojan:Win32/Test", 5, Now.AddDays(-3), "Handled", @"C:\x.exe")],
            empty ? [] :
            [
                new AuditEntry { Timestamp = Now.AddDays(-2), Who = "alice", Command = "EnableRealtimeProtection", Outcome = AuditOutcome.Succeeded },
                new AuditEntry { Timestamp = Now.AddDays(-1), Who = "Planificateur", Command = "StartQuickScan:completed", Outcome = AuditOutcome.Succeeded },
                new AuditEntry { Timestamp = Now, Who = "alice", Command = "undo:DisableStartupItem", Outcome = AuditOutcome.Failed },
            ]);
    }

    private static string Translate(string key) => key switch
    {
        "Report_Title" => "Rapport {0}",
        "Report_PeriodMonth" or "Report_PeriodWeek" => "Du {0} au {1}",
        "Report_Machine" => "Ordinateur : {0}",
        "Report_ScoreExplanation" => "{0} problème(s)",
        "Report_GeneratedBy" => "Généré par {0} le {1}",
        "Issue_LowDiskSpace_Title" => "Il reste {0} Go",
        "Command_UndoOf" => "Annulation : {0}",
        _ => key,
    };

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("ar-MA")]
    public void Pdf_genere_dans_chaque_langue(string culture)
    {
        var pdf = ReportPdfBuilder.Build(Sample(), Translate, CultureInfo.GetCultureInfo(culture));

        pdf.Length.Should().BeGreaterThan(2000);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Pdf_sans_donnees()
    {
        var pdf = ReportPdfBuilder.Build(Sample(empty: true), Translate, CultureInfo.GetCultureInfo("fr-FR"));

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Libelles_des_commandes()
    {
        ReportPdfBuilder.CommandLabel("EnableRealtimeProtection", Translate).Should().Be("Command_EnableRealtimeProtection");
        ReportPdfBuilder.CommandLabel("StartQuickScan:completed", Translate).Should().Be("Command_StartQuickScan");
        ReportPdfBuilder.CommandLabel("undo:SetPowerPlan", Translate).Should().Be("Annulation : Command_SetPowerPlan");
    }
}
