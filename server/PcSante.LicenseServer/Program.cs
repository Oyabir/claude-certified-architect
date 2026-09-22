using PcSante.LicenseServer;

if (args.Length > 0 && args[0] == "keygen")
{
    LicenseServerApp.PrintNewKeys(Console.Out);
    return;
}

var app = LicenseServerApp.Build(args);
await app.RunAsync().ConfigureAwait(false);

/// <summary>Point d'entrée (exposé pour les tests d'intégration).</summary>
public partial class Program;
