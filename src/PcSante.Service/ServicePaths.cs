using PcSante.Core;

namespace PcSante.Service;

/// <summary>Emplacements du service. Le dossier de données est réservé à SYSTEM et aux administrateurs.</summary>
public sealed class ServicePaths
{
    public ServicePaths(string dataRoot, string installDirectory)
    {
        DataRoot = dataRoot;
        InstallDirectory = installDirectory;
    }

    public string DataRoot { get; }

    public string InstallDirectory { get; }

    public string Database => Path.Combine(DataRoot, "pcsante.db");

    public string Logs => Path.Combine(DataRoot, "logs");

    public string Backups => Path.Combine(DataRoot, "backups");

    public string Updates => Path.Combine(DataRoot, "updates");

    public string LicenseState => Path.Combine(DataRoot, "license.dat");

    public string ServiceExecutable => Path.Combine(InstallDirectory, "PcSante.Service.exe");

    public static ServicePaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductInfo.TechnicalName),
        AppContext.BaseDirectory);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Updates);
    }
}
