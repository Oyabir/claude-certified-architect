using System.Text.Json;

namespace PcSante.Core.Settings;

/// <summary>Lecture/écriture des préférences utilisateur. Un fichier illisible redonne les valeurs par défaut.</summary>
public sealed class SettingsStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductInfo.TechnicalName,
        "settings.json");

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(FilePath);
            return (JsonSerializer.Deserialize<UserSettings>(json, PcSanteJson.Options) ?? new UserSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), PcSanteJson.Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
