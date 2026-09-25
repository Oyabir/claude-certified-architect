using PcSante.PmeConsole;
using PcSante.PmeConsole.Services;

// Administration du serveur : « create-organization "<nom>" <e-mail du gérant> <nombre de postes> ».
// Affiche une seule fois le mot de passe provisoire du gérant et le code d'inscription des postes.
if (args.Length == 4 && args[0] == "create-organization")
{
    if (!int.TryParse(args[3], out var seats))
    {
        Console.Error.WriteLine("Nombre de postes invalide.");
        return 2;
    }

    var admin = PmeConsoleApp.Build([]);
    using var scope = admin.Services.CreateScope();
    var created = await scope.ServiceProvider.GetRequiredService<ManagerService>().CreateOrganizationAsync(args[1], args[2], seats, default).ConfigureAwait(false);
    Console.WriteLine($"Organisation créée : {args[1]} ({seats} postes)");
    Console.WriteLine($"Gérant : {created.OwnerEmail}");
    Console.WriteLine($"Mot de passe provisoire (à changer à la première connexion) : {created.TemporaryPassword}");
    Console.WriteLine($"Code d'inscription des postes : {created.EnrollmentCode}");
    return 0;
}

var app = PmeConsoleApp.Build(args);
await app.RunAsync().ConfigureAwait(false);
return 0;

/// <summary>Point d'entrée (exposé pour les tests d'intégration).</summary>
public partial class Program;
