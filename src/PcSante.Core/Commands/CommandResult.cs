using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcSante.Core.Commands;

public enum CommandStatus
{
    /// <summary>Action réalisée et contrôlée.</summary>
    Succeeded,

    /// <summary>Rien à faire : le système est déjà dans l'état souhaité.</summary>
    AlreadyDone,

    /// <summary>Action tentée mais échouée (rien n'a été laissé à moitié : voir <see cref="CommandResult.MessageKey"/>).</summary>
    Failed,

    /// <summary>Commande refusée avant exécution (hors catalogue, paramètres, licence, sécurité).</summary>
    Refused,

    /// <summary>Action lancée en arrière-plan (scan long) : le résultat arrivera dans l'historique.</summary>
    Started,
}

/// <summary>Raison d'échec ou de refus, sans code brut destiné à l'utilisateur.</summary>
public enum FailureReason
{
    None,
    NotInCatalog,
    InvalidParameters,
    ConfirmationRequired,
    LicenseRequired,
    NotSupportedOnThisPc,
    BlockedByWindows,
    PreconditionFailed,
    SafeguardFailed,
    ExecutionFailed,
    VerificationFailed,
    ClientNotTrusted,
    ProtectedItem,
    NotFound,
    ServiceUnavailable,
    NetworkUnavailable,
    LicenseInvalidKey,
    LicenseUsedElsewhere,
    LicenseRevoked,
    LicenseExpired,
    LicenseTransferLimit,
    LicenseNotConfigured,
    InternalError,
}

/// <summary>
/// Résultat d'une commande. Le texte affiché est résolu côté interface à partir de <see cref="MessageKey"/>
/// (fichiers de ressources) ; <see cref="TechnicalDetails"/> n'apparaît que derrière le lien « Détails ».
/// </summary>
public sealed record CommandResult
{
    public required CommandStatus Status { get; init; }

    public FailureReason Reason { get; init; } = FailureReason.None;

    /// <summary>Clé de ressource du message clair (ex. « Result_RealtimeEnabled »).</summary>
    public required string MessageKey { get; init; }

    public IReadOnlyList<string> MessageArgs { get; init; } = [];

    public string? TechnicalDetails { get; init; }

    /// <summary>Identifiant d'annulation si l'action est réversible.</summary>
    public Guid? UndoId { get; init; }

    /// <summary>Données renvoyées par les requêtes de lecture.</summary>
    public JsonElement? Data { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Status is CommandStatus.Succeeded or CommandStatus.AlreadyDone or CommandStatus.Started;

    public static CommandResult Success(string messageKey, params string[] args) =>
        new() { Status = CommandStatus.Succeeded, MessageKey = messageKey, MessageArgs = args };

    public static CommandResult WithData<T>(T data, string messageKey = "Result_Ok") =>
        new()
        {
            Status = CommandStatus.Succeeded,
            MessageKey = messageKey,
            Data = JsonSerializer.SerializeToElement(data, PcSanteJson.Options),
        };

    public static CommandResult Refused(FailureReason reason, string messageKey, string? details = null) =>
        new() { Status = CommandStatus.Refused, Reason = reason, MessageKey = messageKey, TechnicalDetails = details };

    public static CommandResult Failure(FailureReason reason, string messageKey, string? details = null) =>
        new() { Status = CommandStatus.Failed, Reason = reason, MessageKey = messageKey, TechnicalDetails = details };

    public T? GetData<T>() => Data is { } d ? d.Deserialize<T>(PcSanteJson.Options) : default;
}
