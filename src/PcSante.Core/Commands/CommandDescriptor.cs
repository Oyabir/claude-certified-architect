namespace PcSante.Core.Commands;

/// <summary>Lecture seule (aucune modification du système) ou action modifiant le système.</summary>
public enum CommandKind
{
    Query,
    Action,
}

/// <summary>Offre minimale requise pour exécuter la commande (section 6).</summary>
public enum RequiredTier
{
    Free,
    Premium,
}

/// <summary>Sauvegarde exigée avant l'exécution (cycle vérifier → sauvegarder → exécuter → contrôler → journaliser).</summary>
public enum SafeguardKind
{
    /// <summary>Aucune modification de configuration (scan, recherche, lecture).</summary>
    None,

    /// <summary>Point de restauration système obligatoire avant exécution.</summary>
    RestorePoint,

    /// <summary>L'action sauvegarde elle-même l'état précédent et sait l'annuler.</summary>
    OwnBackup,

    /// <summary>Point de restauration ET sauvegarde propre (annulation par action).</summary>
    RestorePointAndOwnBackup,
}

/// <summary>Raison pour laquelle l'utilisateur doit confirmer explicitement (section 11).</summary>
public enum ConfirmationKind
{
    None,
    ClosesProgram,
    RestartsComputer,
    DeletesFiles,
    ReducesProtection,

    /// <summary>Chiffrement du disque : rappel impératif de garder la clé de récupération.</summary>
    EncryptsDisk,
}

/// <summary>Type d'un paramètre de commande. Aucun type « texte libre » exécutable n'existe.</summary>
public enum ParameterType
{
    /// <summary>Entier positif.</summary>
    PositiveInteger,

    /// <summary>Valeur parmi une liste fermée.</summary>
    Choice,

    /// <summary>Identifiant opaque : lettres, chiffres, espaces et . _ - { } : \ / | ( ), 256 caractères max, sans « .. ».</summary>
    Identifier,

    /// <summary>Chemin local absolu d'un dossier ou fichier existant (pas de chemin réseau).</summary>
    LocalPath,

    /// <summary>Clé de licence au format PCS-XXXXX-XXXXX-XXXXX-XXXXX.</summary>
    LicenseKey,

    /// <summary>Heure au format HH:mm.</summary>
    TimeOfDay,

    /// <summary>Booléen « true » / « false ».</summary>
    Boolean,

    /// <summary>GUID.</summary>
    Guid,
}

public sealed record ParameterSpec(
    string Name,
    ParameterType Type,
    bool Required = true,
    IReadOnlyList<string>? AllowedValues = null);

/// <summary>Description complète d'une commande du catalogue fermé.</summary>
public sealed record CommandDescriptor(
    CommandId Id,
    CommandKind Kind,
    RequiredTier Tier,
    SafeguardKind Safeguard,
    ConfirmationKind Confirmation,
    IReadOnlyList<ParameterSpec> Parameters)
{
    public bool RequiresConfirmation => Confirmation != ConfirmationKind.None;

    public bool IsUndoable => Safeguard is SafeguardKind.OwnBackup or SafeguardKind.RestorePointAndOwnBackup;
}
