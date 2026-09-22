using System.Globalization;
using PcSante.Core;
using PcSante.Core.Audit;
using PcSante.Core.Health;
using PcSante.Core.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PcSante.Reporting;

/// <summary>
/// Rapport PDF hebdomadaire ou mensuel (M8), lisible par un non-technicien :
/// score en grand avec sa couleur, problèmes expliqués, menaces, actions réalisées, évolution du score.
/// Aucun texte en dur : tout passe par <paramref name="translate"/> (fichiers de ressources de l'interface).
/// </summary>
public static class ReportPdfBuilder
{
    private static readonly string Green = "#1E7B34";
    private static readonly string Orange = "#B25E00";
    private static readonly string Red = "#B42318";

    static ReportPdfBuilder()
    {
        // Licence Community de QuestPDF (gratuite, section 10).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(ReportData data, Func<string, string> translate, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(translate);
        ArgumentNullException.ThrowIfNull(culture);
        var rtl = culture.TextInfo.IsRightToLeft;
        string T(string key) => translate(key);
        string F(string key, params object[] args) => string.Format(culture, translate(key), args);

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Segoe UI", "Arial", "Noto Sans", "Noto Naskh Arabic", "DejaVu Sans"));
            if (rtl)
            {
                page.ContentFromRightToLeft();
            }

            page.Header().Column(header =>
            {
                header.Item().Text(F("Report_Title", ProductInfo.Name)).FontSize(22).SemiBold();
                header.Item().Text(F(data.Period == ReportPeriod.Week ? "Report_PeriodWeek" : "Report_PeriodMonth",
                    data.From.ToLocalTime().ToString("d", culture), data.To.ToLocalTime().ToString("d", culture))).FontColor(Colors.Grey.Darken2);
                header.Item().Text(F("Report_Machine", data.MachineName)).FontColor(Colors.Grey.Darken2);
            });

            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(14);
                ScoreSection(col, data.CurrentHealth, T, F);
                HistorySection(col, data.ScoreHistory, T, culture);
                IssuesSection(col, data.CurrentHealth, T, culture);
                ThreatsSection(col, data, T, culture);
                ActionsSection(col, data.Actions, T, culture);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(F("Report_GeneratedBy", ProductInfo.Name, DateTimeOffset.Now.ToString("g", culture))).FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }));

        return document.GeneratePdf();
    }

    private static string ColorOf(HealthColor color) => color switch
    {
        HealthColor.Green => Green,
        HealthColor.Orange => Orange,
        _ => Red,
    };

    private static void ScoreSection(ColumnDescriptor col, HealthReport? health, Func<string, string> T, Func<string, object[], string> F)
    {
        col.Item().Text(T("Report_ScoreTitle")).FontSize(15).SemiBold();
        if (health is null)
        {
            col.Item().Text(T("Report_NoAnalysis"));
            return;
        }

        col.Item().Row(row =>
        {
            row.ConstantItem(120).Border(2).BorderColor(ColorOf(health.Color)).Padding(10).AlignCenter().Column(c =>
            {
                c.Item().AlignCenter().Text(health.Score.ToString(CultureInfo.InvariantCulture)).FontSize(36).Bold().FontColor(ColorOf(health.Color));
                c.Item().AlignCenter().Text(T($"Color_{health.Color}")).FontColor(ColorOf(health.Color));
            });
            row.RelativeItem().PaddingHorizontal(16).Column(c =>
            {
                c.Spacing(4);
                SubScore(c, T("Category_Security"), health.SubScores.Security);
                SubScore(c, T("Category_Performance"), health.SubScores.Performance);
                SubScore(c, T("Category_Stability"), health.SubScores.Stability);
                SubScore(c, T("Category_Storage"), health.SubScores.Storage);
            });
        });
        col.Item().Text(F("Report_ScoreExplanation", [health.Issues.Count])).FontColor(Colors.Grey.Darken2);
    }

    private static void SubScore(ColumnDescriptor c, string label, int value)
    {
        var color = ColorOf(HealthScoreCalculator.ColorOf(value));
        c.Item().Row(r =>
        {
            r.ConstantItem(110).Text(label);
            r.RelativeItem().Height(12).Background(Colors.Grey.Lighten3).Row(bar =>
            {
                if (value > 0)
                {
                    bar.RelativeItem(value).Background(color);
                }

                if (value < 100)
                {
                    bar.RelativeItem(100 - value);
                }
            });
            r.ConstantItem(40).AlignRight().Text(value.ToString(CultureInfo.InvariantCulture)).FontColor(color).SemiBold();
        });
    }

    private static void HistorySection(ColumnDescriptor col, IReadOnlyList<ScorePoint> history, Func<string, string> T, CultureInfo culture)
    {
        col.Item().Text(T("Report_HistoryTitle")).FontSize(15).SemiBold();
        if (history.Count == 0)
        {
            col.Item().Text(T("Report_NoHistory"));
            return;
        }

        // Un point par jour (dernier score de la journée), sous forme de barres lisibles.
        var daily = history.GroupBy(p => p.At.ToLocalTime().Date).Select(g => g.OrderBy(p => p.At).Last()).TakeLast(31).ToList();
        col.Item().Height(110).Row(row =>
        {
            foreach (var point in daily)
            {
                row.RelativeItem().PaddingHorizontal(1).AlignBottom().Column(c =>
                {
                    c.Item().Height((float)Math.Max(2, point.Score)).Background(ColorOf(HealthScoreCalculator.ColorOf(point.Score)));
                });
            }
        });
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(daily[0].At.ToLocalTime().ToString("d", culture)).FontSize(9);
            r.RelativeItem().AlignRight().Text(daily[^1].At.ToLocalTime().ToString("d", culture)).FontSize(9);
        });
    }

    private static void IssuesSection(ColumnDescriptor col, HealthReport? health, Func<string, string> T, CultureInfo culture)
    {
        col.Item().Text(T("Report_IssuesTitle")).FontSize(15).SemiBold();
        if (health is null || health.Issues.Count == 0)
        {
            col.Item().Text(T("Report_NoIssues")).FontColor(Green);
            return;
        }

        foreach (var issue in health.Issues)
        {
            col.Item().BorderLeft(4).BorderColor(ColorOf(HealthScoreCalculator.ColorOf(issue.Severity))).PaddingLeft(8).Column(c =>
            {
                c.Item().Text(Format(T(issue.TitleKey), issue.Args, culture)).SemiBold();
                c.Item().Text(Format(T(issue.WhyKey), issue.Args, culture)).FontColor(Colors.Grey.Darken2);
            });
        }
    }

    private static void ThreatsSection(ColumnDescriptor col, ReportData data, Func<string, string> T, CultureInfo culture)
    {
        col.Item().Text(T("Report_ThreatsTitle")).FontSize(15).SemiBold();
        if (data.Threats.Count == 0)
        {
            col.Item().Text(T("Report_NoThreats")).FontColor(Green);
            return;
        }

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(90);
                c.RelativeColumn(2);
                c.RelativeColumn();
            });
            table.Header(h =>
            {
                h.Cell().Text(T("Report_Date")).SemiBold();
                h.Cell().Text(T("Report_Threat")).SemiBold();
                h.Cell().Text(T("Report_Status")).SemiBold();
            });
            foreach (var t in data.Threats)
            {
                table.Cell().Text(t.DetectedAt.ToLocalTime().ToString("d", culture));
                table.Cell().Text(t.Name);
                table.Cell().Text(T($"Threat_{t.Status}"));
            }
        });
    }

    private static void ActionsSection(ColumnDescriptor col, IReadOnlyList<AuditEntry> actions, Func<string, string> T, CultureInfo culture)
    {
        col.Item().Text(T("Report_ActionsTitle")).FontSize(15).SemiBold();
        if (actions.Count == 0)
        {
            col.Item().Text(T("Report_NoActions"));
            return;
        }

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(110);
                c.RelativeColumn(2);
                c.RelativeColumn();
            });
            table.Header(h =>
            {
                h.Cell().Text(T("Report_Date")).SemiBold();
                h.Cell().Text(T("Report_Action")).SemiBold();
                h.Cell().Text(T("Report_Result")).SemiBold();
            });
            foreach (var a in actions.Take(60))
            {
                table.Cell().Text(a.Timestamp.ToLocalTime().ToString("g", culture));
                table.Cell().Text(CommandLabel(a.Command, T));
                table.Cell().Text(T($"Outcome_{a.Outcome}")).FontColor(a.Outcome == AuditOutcome.Succeeded ? Green : Red);
            }
        });
    }

    /// <summary>Libellé lisible d'une commande journalisée (« undo:X » et « X:completed » compris).</summary>
    public static string CommandLabel(string command, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(translate);
        if (command.StartsWith("undo:", StringComparison.Ordinal))
        {
            return string.Format(CultureInfo.CurrentCulture, translate("Command_UndoOf"), translate("Command_" + command[5..]));
        }

        var name = command.EndsWith(":completed", StringComparison.Ordinal) ? command[..^10] : command;
        return translate("Command_" + name);
    }

    private static string Format(string template, IReadOnlyList<string> args, CultureInfo culture)
    {
        try
        {
            return args.Count == 0 ? template : string.Format(culture, template, args.Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
