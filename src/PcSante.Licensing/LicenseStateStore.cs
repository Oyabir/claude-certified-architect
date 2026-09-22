using System.Text;
using System.Text.Json;
using PcSante.Core;

namespace PcSante.Licensing;

/// <summary>Chiffrement lié à la machine (DPAPI LocalMachine sous Windows).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    /// <returns>null si les données ne peuvent pas être déchiffrées (copiées d'un autre PC, altérées).</returns>
    byte[]? Unprotect(byte[] data);
}

/// <summary>État persistant de la licence sur le PC.</summary>
public sealed record StoredLicenseState
{
    public string? Token { get; init; }

    /// <summary>Plus grande date observée (détection du recul d'horloge).</summary>
    public DateTimeOffset HighWaterMark { get; init; }

    public DateTimeOffset? LastValidatedAt { get; init; }

    /// <summary>Révocation connue : l'offre Gratuite s'applique jusqu'à une nouvelle activation.</summary>
    public bool Revoked { get; init; }

    public bool ClockTampered { get; init; }
}

public interface ILicenseStateStore
{
    StoredLicenseState Load();

    void Save(StoredLicenseState state);
}

/// <summary>Fichier chiffré (DPAPI machine) dans le dossier de données du service.</summary>
public sealed class ProtectedFileLicenseStateStore(string filePath, ISecretProtector protector) : ILicenseStateStore
{
    public StoredLicenseState Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new StoredLicenseState();
            }

            var clear = protector.Unprotect(File.ReadAllBytes(filePath));
            if (clear is null)
            {
                return new StoredLicenseState();
            }

            return JsonSerializer.Deserialize<StoredLicenseState>(Encoding.UTF8.GetString(clear), PcSanteJson.Options) ?? new StoredLicenseState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new StoredLicenseState();
        }
    }

    public void Save(StoredLicenseState state)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var data = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state, PcSanteJson.Options));
        var temp = filePath + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, filePath, overwrite: true);
    }
}

/// <summary>Stockage en mémoire (tests).</summary>
public sealed class InMemoryLicenseStateStore : ILicenseStateStore
{
    private StoredLicenseState _state = new();

    public StoredLicenseState Load() => _state;

    public void Save(StoredLicenseState state) => _state = state;
}
