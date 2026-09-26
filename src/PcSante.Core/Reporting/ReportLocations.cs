namespace PcSante.Core.Reporting;

/// <summary>Emplacement des rapports mensuels produits par la tâche planifiée (partagé par le service et l'interface).</summary>
public static class ReportLocations
{
    /// <summary>Documents publics\&lt;Produit&gt;\Rapports : le service (SYSTEM) y écrit, chaque compte du PC peut les lire.</summary>
    public static string MonthlyReportsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), ProductInfo.Name, "Rapports");

    /// <summary>Nom du fichier d'un rapport mensuel (un par mois ; une nouvelle exécution le remplace).</summary>
    public static string MonthlyReportFileName(DateTimeOffset at) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ProductInfo.TechnicalName}-rapport-{at:yyyy-MM}.pdf");
}
