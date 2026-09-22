using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using PcSante.Ipc.Security;
using Xunit;

namespace PcSante.App.Tests;

/// <summary>
/// L'installeur WiX ne se compile que sous Windows : ces tests vérifient sa structure partout
/// (installation du service SYSTEM, mise à jour, désinstallation propre, textes localisés).
/// </summary>
public partial class InstallerStructureTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";
    private static readonly string Root = FindRoot();
    private static readonly XDocument Package = XDocument.Load(Path.Combine(Root, "installer", "Package.wxs"));

    [Fact]
    public void Service_installe_en_SYSTEM_demarrage_automatique_et_retire_a_la_desinstallation()
    {
        var install = Package.Descendants(Wix + "ServiceInstall").Single();
        install.Attribute("Account")!.Value.Should().Be("LocalSystem");
        install.Attribute("Start")!.Value.Should().Be("auto");
        install.Descendants(Util + "ServiceConfig").Should().ContainSingle();

        var control = Package.Descendants(Wix + "ServiceControl").Single();
        control.Attribute("Remove")!.Value.Should().Be("uninstall");
        control.Attribute("Stop")!.Value.Should().Be("both");
    }

    [Fact]
    public void Mise_a_jour_et_desinstallation_propre()
    {
        Package.Descendants(Wix + "MajorUpgrade").Should().ContainSingle();
        Package.Descendants(Wix + "Package").Single().Attribute("UpgradeCode")!.Value.Should().MatchRegex("^[0-9A-F-]{36}$");
        Package.Descendants(Util + "RemoveFolderEx").Single().Attribute("Condition")!.Value.Should().Contain("NOT UPGRADINGPRODUCTCODE");
        var cleanup = Package.Descendants(Wix + "CustomAction").Single(c => (string?)c.Attribute("Id") == "UninstallCleanup");
        cleanup.Attribute("ExeCommand")!.Value.Should().Be("--uninstall-cleanup");
        cleanup.Attribute("Impersonate")!.Value.Should().Be("no");
    }

    [Fact]
    public void Runtime_dotnet_verifie_avant_installation()
    {
        Package.ToString().Should().Contain("DotNetCompatibilityCheck").And.Contain("RuntimeType=\"desktop\"");
    }

    [Fact]
    public void Tous_les_textes_localises_existent()
    {
        var wxl = XDocument.Load(Path.Combine(Root, "installer", "Package.fr-FR.wxl"));
        var defined = wxl.Descendants().Where(e => e.Name.LocalName == "String").Select(e => (string)e.Attribute("Id")!).ToHashSet();
        var used = LocRef().Matches(Package.ToString()).Select(m => m.Groups[1].Value).Distinct();

        used.Should().OnlyContain(id => defined.Contains(id));
    }

    [Fact]
    public void Executables_autorises_par_le_pipe_correspondent_aux_projets()
    {
        var names = new[] { "PcSante.App", "PcSante.Service", "PcSante.Overlay" }
            .Select(p => XDocument.Load(Path.Combine(Root, "src", p, p + ".csproj")).Descendants("AssemblyName").Single().Value + ".exe");

        names.Should().BeEquivalentTo(ClientTrustPolicy.ApplicationExecutables);
    }

    [Fact]
    public void Script_de_construction_signe_les_binaires_et_le_msi()
    {
        var script = File.ReadAllText(Path.Combine(Root, "installer", "build.ps1"));
        script.Should().Contain("signtool.exe sign").And.Contain("PCSANTE_SIGN_THUMBPRINT").And.Contain("--self-contained false");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcSante.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("PcSante.sln introuvable");
    }

    [GeneratedRegex(@"!\(loc\.([A-Za-z]+)\)")]
    private static partial Regex LocRef();
}
