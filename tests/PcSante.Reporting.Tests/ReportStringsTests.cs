using FluentAssertions;
using Xunit;
using PcSante.Reporting;

namespace PcSante.Reporting.Tests;

public class ReportStringsTests
{
    [Theory]
    [InlineData("fr", "Rapport mensuel")]
    [InlineData("en", "Monthly report")]
    [InlineData("ar", "التقرير الشهري")]
    public void Traductions_de_l_interface_disponibles_sans_l_interface(string language, string expected)
    {
        ReportStrings.For(ReportStrings.CultureOf(language))("Template_MonthlyReport").Should().Be(expected);
    }

    [Fact]
    public void Langue_inconnue_en_francais_et_cle_absente_sans_exception()
    {
        ReportStrings.CultureOf("de").Name.Should().Be("fr");
        ReportStrings.CultureOf(null).Name.Should().Be("fr");
        ReportStrings.For(ReportStrings.CultureOf("fr"))("Cle_inexistante").Should().Be("Cle_inexistante");
    }
}
