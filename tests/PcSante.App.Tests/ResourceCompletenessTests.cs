using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Licensing;
using PcSante.Core.Processes;
using PcSante.Core.Scheduling;
using PcSante.Core.Settings;
using PcSante.Core.Ui;
using PcSante.Core.Windows;
using Xunit;

namespace PcSante.App.Tests;

/// <summary>
/// « Aucun texte en dur » et interface en français, anglais et arabe (sections 10 et 13) :
/// chaque clé utilisée par le code existe dans les trois langues avec les mêmes paramètres.
/// </summary>
public partial class ResourceCompletenessTests
{
    private static readonly string Root = FindRoot();
    private static readonly Dictionary<string, Dictionary<string, string>> Languages = new()
    {
        ["fr"] = Load("Strings.resx"),
        ["en"] = Load("Strings.en.resx"),
        ["ar"] = Load("Strings.ar.resx"),
    };

    private static Dictionary<string, string> French => Languages["fr"];

    [Fact]
    public void Memes_cles_dans_les_trois_langues_et_aucune_vide()
    {
        foreach (var (lang, strings) in Languages)
        {
            strings.Keys.Should().BeEquivalentTo(French.Keys, $"la langue {lang} doit avoir toutes les clés");
            strings.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v), $"aucun texte vide en {lang}");
        }
    }

    [Fact]
    public void Memes_parametres_dans_les_trois_langues()
    {
        foreach (var key in French.Keys)
        {
            var expected = Placeholders(French[key]);
            Placeholders(Languages["en"][key]).Should().BeEquivalentTo(expected, $"paramètres de {key} (en)");
            Placeholders(Languages["ar"][key]).Should().BeEquivalentTo(expected, $"paramètres de {key} (ar)");
        }
    }

    [Fact]
    public void L_arabe_est_reellement_traduit()
    {
        var arabic = Languages["ar"].Values.Count(v => v.Any(c => c is >= '؀' and <= 'ۿ'));
        arabic.Should().BeGreaterThan((int)(French.Count * 0.9));
    }

    [Fact]
    public void Toutes_les_cles_litterales_du_code_existent()
    {
        var missing = SourceFiles()
            .SelectMany(f => KeyLiteral().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)
                .Concat(XamlKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)))
            .Distinct()
            .Where(k => !French.ContainsKey(k))
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Familles_de_cles_dynamiques_completes()
    {
        var expected = new List<string>();
        expected.AddRange(Enum.GetValues<ScreenId>().SelectMany(s => new[] { $"Nav_{s}", $"Subtitle_{s}" }));
        expected.AddRange(Enum.GetValues<CommandId>().Select(c => $"Command_{c}"));
        expected.AddRange(Enum.GetValues<ConfirmationKind>().Where(c => c != ConfirmationKind.None).Select(c => $"Confirm_{c}"));
        expected.AddRange(Enum.GetValues<HealthCategory>().Select(c => $"Category_{c}"));
        expected.AddRange(Enum.GetValues<HealthColor>().Select(c => $"Color_{c}"));
        expected.AddRange(Enum.GetValues<LicenseTier>().Select(c => $"Tier_{c}"));
        expected.AddRange(Enum.GetValues<LicenseState>().Select(c => $"License_State_{c}"));
        expected.AddRange(Enum.GetValues<AuditOutcome>().Select(c => $"Outcome_{c}"));
        expected.AddRange(Enum.GetValues<Reputation>().Select(c => $"Reputation_{c}"));
        expected.AddRange(Enum.GetValues<StartupLocation>().Select(c => $"StartupLocation_{c}"));
        expected.AddRange(Enum.GetValues<FirewallProfile>().SelectMany(p => new[] { $"Firewall_{p}", $"Firewall_{p}Tech" }));
        expected.AddRange(Enum.GetValues<ScheduledTemplateId>().SelectMany(t => new[] { $"Template_{t}", $"Template_{t}_Why" }));
        expected.AddRange(CommandDefinitions.Days.Select(d => $"Day_{d}"));
        expected.AddRange(Enum.GetValues<AppTheme>().Select(t => $"Theme_{t}"));
        expected.AddRange(Enum.GetValues<OverlayPosition>().Select(p => $"Position_{p}"));
        expected.AddRange(new[] { "Handled", "ActionRequired" }.Select(s => $"Threat_{s}"));
        CommandId[] systemActions = [CommandId.InstallUpdates, CommandId.RepairWindowsUpdate, CommandId.RunSystemFileCheck, CommandId.RunDismRepair,
            CommandId.CreateRestorePoint, CommandId.EnableSystemRestore, CommandId.ResetFirewallRules, CommandId.FlushDnsCache, CommandId.ResetNetworkStack];
        expected.AddRange(systemActions.SelectMany(c => new[] { $"Explain_{c}", $"Duration_{c}", $"Busy_{c}" }));

        expected.Where(k => !French.ContainsKey(k)).Should().BeEmpty();
    }

    [Fact]
    public void Chaque_probleme_detectable_a_titre_explication_et_bouton()
    {
        var rules = File.ReadAllText(Path.Combine(Root, "src", "PcSante.Core", "Health", "DiagnosticRules.cs"));
        var codes = IssueCode().Matches(rules).Select(m => m.Groups[1].Value)
            .Concat(Enum.GetValues<FirewallProfile>().Select(p => $"FirewallOff{p}"))
            .Distinct()
            .ToList();
        codes.Should().HaveCountGreaterThan(10);
        foreach (var code in codes)
        {
            French.Should().ContainKey($"Issue_{code}_Title");
            French.Should().ContainKey($"Issue_{code}_Why");
        }

        foreach (var command in FixCommand().Matches(rules).Select(m => m.Groups[1].Value).Distinct())
        {
            French.Should().ContainKey($"Fix_{command}");
        }
    }

    [Fact]
    public void Tous_les_messages_du_service_sont_traduits()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var keys = files.SelectMany(f => ResultKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();

        keys.Should().HaveCountGreaterThan(50);
        keys.Where(k => !French.ContainsKey(k)).Should().BeEmpty();
    }

    [Fact]
    public void Vocabulaire_simple_terme_technique_en_petit()
    {
        French["Protection_Firewall"].Should().Be("Protection contre les intrusions");
        French["Protection_FirewallTech"].Should().Be("Pare-feu Windows");
        French["License_UsedElsewhere"].Should().Be("Cette licence est déjà utilisée sur un autre ordinateur.");
    }

    [Fact]
    public void Aucun_code_d_erreur_brut_dans_les_messages()
    {
        French.Where(kv => kv.Key.StartsWith("Result_", StringComparison.Ordinal))
            .Should().OnlyContain(kv => !HexCode().IsMatch(kv.Value));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", "PcSante.App"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).ToHashSet();

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(Root, "src", "PcSante.App", "Resources", file)).Root!
            .Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcSante.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("PcSante.sln introuvable");
    }

    [GeneratedRegex(@"Loc\.[TF]\(""([A-Za-z0-9_]+)""")]
    private static partial Regex KeyLiteral();

    [GeneratedRegex(@"\{l:T ([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"Issue\(""([A-Za-z]+)""")]
    private static partial Regex IssueCode();

    [GeneratedRegex(@"IssueFix\.Run\(CommandId\.([A-Za-z]+)")]
    private static partial Regex FixCommand();

    [GeneratedRegex(@"""((?:Result|License)_[A-Za-z]+)""")]
    private static partial Regex ResultKey();

    [GeneratedRegex(@"0x[0-9A-Fa-f]{4,}|HRESULT")]
    private static partial Regex HexCode();
}
