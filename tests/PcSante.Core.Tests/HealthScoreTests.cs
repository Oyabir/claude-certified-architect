using PcSante.Core.Health;

namespace PcSante.Core.Tests;

public class HealthScoreTests
{
    private static HealthIssue I(HealthCategory c, IssueSeverity s) => new("X", c, s, null, []);

    [Fact]
    public void Aucun_probleme_donne_100_vert()
    {
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch, [], TimeSpan.Zero);

        report.Score.Should().Be(100);
        report.Color.Should().Be(HealthColor.Green);
        report.SubScores.Should().Be(new SubScores(100, 100, 100, 100));
    }

    [Fact]
    public void Probleme_rouge_donne_un_score_rouge()
    {
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch, [I(HealthCategory.Storage, IssueSeverity.Critical)], TimeSpan.Zero);

        report.Score.Should().BeLessThan(HealthScoreCalculator.OrangeThreshold);
        report.Color.Should().Be(HealthColor.Red);
        report.SubScores.Storage.Should().Be(49, "un problème rouge rend aussi son sous-score rouge");
    }

    [Fact]
    public void Probleme_orange_empeche_le_vert()
    {
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch, [I(HealthCategory.Performance, IssueSeverity.Warning)], TimeSpan.Zero);

        report.Score.Should().Be(79);
        report.Color.Should().Be(HealthColor.Orange);
    }

    [Fact]
    public void Sous_score_avec_probleme_orange_n_est_jamais_vert()
    {
        // Cas vu en test : restauration désactivée → Stabilité à 80 (vert) alors que le problème est orange.
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch, [I(HealthCategory.Stability, IssueSeverity.Warning)], TimeSpan.Zero);

        report.SubScores.Stability.Should().Be(79);
        HealthScoreCalculator.ColorOf(report.SubScores.Stability).Should().Be(HealthColor.Orange);
        report.SubScores.Security.Should().Be(100, "les autres catégories ne sont pas touchées");
        report.Color.Should().Be(HealthColor.Orange);
    }

    [Fact]
    public void Information_reste_verte()
    {
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch, [I(HealthCategory.Stability, IssueSeverity.Info)], TimeSpan.Zero);

        report.Score.Should().Be(99);
        report.Color.Should().Be(HealthColor.Green);
    }

    [Fact]
    public void Sous_score_borne_a_zero()
    {
        var issues = Enumerable.Repeat(I(HealthCategory.Security, IssueSeverity.Critical), 5).ToList();

        HealthScoreCalculator.ComputeSubScores(issues).Security.Should().Be(0);
        HealthScoreCalculator.ComputeScore(HealthScoreCalculator.ComputeSubScores(issues), issues).Should().Be(49);
    }

    [Fact]
    public void Ponderation_des_sous_scores()
    {
        HealthScoreCalculator.ComputeScore(new SubScores(0, 100, 100, 100), []).Should().Be(65);
        HealthScoreCalculator.ComputeScore(new SubScores(100, 0, 100, 100), []).Should().Be(75);
        HealthScoreCalculator.ComputeScore(new SubScores(100, 100, 0, 0), []).Should().Be(60);
    }

    [Theory]
    [InlineData(100, HealthColor.Green)]
    [InlineData(80, HealthColor.Green)]
    [InlineData(79, HealthColor.Orange)]
    [InlineData(50, HealthColor.Orange)]
    [InlineData(49, HealthColor.Red)]
    [InlineData(0, HealthColor.Red)]
    public void Code_couleur(int score, HealthColor color)
    {
        HealthScoreCalculator.ColorOf(score).Should().Be(color);
    }

    [Fact]
    public void Couleur_par_gravite()
    {
        HealthScoreCalculator.ColorOf(IssueSeverity.Critical).Should().Be(HealthColor.Red);
        HealthScoreCalculator.ColorOf(IssueSeverity.Warning).Should().Be(HealthColor.Orange);
        HealthScoreCalculator.ColorOf(IssueSeverity.Info).Should().Be(HealthColor.Green);
    }

    [Fact]
    public void Problemes_tries_par_gravite()
    {
        var report = HealthScoreCalculator.BuildReport(DateTimeOffset.UnixEpoch,
            [I(HealthCategory.Storage, IssueSeverity.Info), I(HealthCategory.Security, IssueSeverity.Critical), I(HealthCategory.Performance, IssueSeverity.Warning)],
            TimeSpan.Zero);

        report.Issues.Select(i => i.Severity).Should().Equal(IssueSeverity.Critical, IssueSeverity.Warning, IssueSeverity.Info);
    }

    [Fact]
    public void Cles_de_ressources_du_probleme()
    {
        var issue = new HealthIssue("RealtimeOff", HealthCategory.Security, IssueSeverity.Critical, null, []);

        issue.TitleKey.Should().Be("Issue_RealtimeOff_Title");
        issue.WhyKey.Should().Be("Issue_RealtimeOff_Why");
    }
}
