using PcSante.Licensing;

namespace PcSante.LicenseServer.Services;

/// <summary>
/// Clés du serveur, lues UNIQUEMENT depuis les variables d'environnement (section 12) :
/// PCSANTE_LICENSE_PRIVATE_KEY (clé privée Ed25519, base64 32 octets) et PCSANTE_ADMIN_API_KEY.
/// </summary>
public sealed class ServerKeys
{
    public const string PrivateKeyVariable = "PCSANTE_LICENSE_PRIVATE_KEY";
    public const string AdminKeyVariable = "PCSANTE_ADMIN_API_KEY";
    public const int MinAdminKeyLength = 32;

    public ServerKeys(byte[] privateKey, string adminApiKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.Length != Ed25519.KeySize)
        {
            throw new InvalidOperationException($"{PrivateKeyVariable} doit contenir 32 octets encodés en base64.");
        }

        if (string.IsNullOrEmpty(adminApiKey) || adminApiKey.Length < MinAdminKeyLength)
        {
            throw new InvalidOperationException($"{AdminKeyVariable} doit contenir au moins {MinAdminKeyLength} caractères.");
        }

        PrivateKey = privateKey;
        PublicKey = Ed25519.PublicKeyFromPrivate(privateKey);
        AdminApiKey = adminApiKey;
    }

    public byte[] PrivateKey { get; }

    public byte[] PublicKey { get; }

    public string AdminApiKey { get; }

    public static ServerKeys FromEnvironment(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);
        var privateText = getVariable(PrivateKeyVariable);
        if (string.IsNullOrWhiteSpace(privateText))
        {
            throw new InvalidOperationException($"Variable d'environnement {PrivateKeyVariable} absente. Générer une paire avec la commande « keygen ».");
        }

        byte[] privateKey;
        try
        {
            privateKey = Convert.FromBase64String(privateText.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{PrivateKeyVariable} n'est pas du base64 valide.", ex);
        }

        return new ServerKeys(privateKey, getVariable(AdminKeyVariable) ?? string.Empty);
    }
}
