namespace PcSante.Core.Windows;

/// <summary>
/// Effets visuels d'un utilisateur (M4). Valeurs du registre de SON profil : masque des préférences, animation des
/// fenêtres, animations de la barre des tâches, choix « personnalisé ». Null = valeur absente (réglage de Windows).
/// </summary>
public sealed record VisualEffectsSettings(byte[]? UserPreferencesMask, string? MinAnimate, int? TaskbarAnimations, int? VisualFxSetting)
{
    /// <summary>
    /// Effets allégés : animations et transparences coupées, lissage du texte CONSERVÉ (FontSmoothing n'est pas touché),
    /// comme « Ajuster afin d'obtenir les meilleures performances » sans rendre le texte difficile à lire.
    /// </summary>
    public static VisualEffectsSettings Light { get; } = new([0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00], "0", 0, 3);

    public bool IsLight => UserPreferencesMask is { } mask && mask.AsSpan().SequenceEqual(Light.UserPreferencesMask) && MinAnimate == Light.MinAnimate;
}

public interface IVisualEffectsApi
{
    /// <summary>Réglages actuels de l'utilisateur ; null si son profil n'est pas chargé (session fermée).</summary>
    Task<VisualEffectsSettings?> ReadAsync(string userSid, CancellationToken cancellationToken);

    Task<bool> WriteAsync(string userSid, VisualEffectsSettings settings, CancellationToken cancellationToken);
}
