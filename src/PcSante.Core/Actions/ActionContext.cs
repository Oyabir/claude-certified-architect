using PcSante.Core.Commands;

namespace PcSante.Core.Actions;

/// <summary>Identité du demandeur, établie par le service (jamais déclarée par le client).</summary>
public sealed record CallerIdentity(string UserName, string? UserSid, int? ProcessId)
{
    public static CallerIdentity Scheduler { get; } = new("Planificateur", null, null);
}

public sealed record ActionContext(CommandDescriptor Descriptor, CommandParameters Parameters, CallerIdentity Caller);

public enum CheckOutcome
{
    /// <summary>L'action peut être exécutée.</summary>
    Proceed,

    /// <summary>Déjà dans l'état souhaité : rien à faire.</summary>
    AlreadyDone,

    /// <summary>Impossible sur ce PC (édition, composant absent, blocage Windows).</summary>
    Blocked,
}

public sealed record CheckResult(CheckOutcome Outcome, FailureReason Reason = FailureReason.None, string? MessageKey = null, string? Details = null)
{
    public static CheckResult Proceed { get; } = new(CheckOutcome.Proceed);

    public static CheckResult AlreadyDone(string messageKey) => new(CheckOutcome.AlreadyDone, MessageKey: messageKey);

    public static CheckResult Blocked(FailureReason reason, string messageKey, string? details = null) =>
        new(CheckOutcome.Blocked, reason, messageKey, details);
}

/// <summary>Données de sauvegarde propres à une action, rejouées par <see cref="SystemAction.UndoAsync"/>.</summary>
public sealed record BackupData(string Description, string Payload);

public sealed record ExecutionResult(bool Success, string MessageKey, string? Details = null, bool StartedInBackground = false, IReadOnlyList<string>? MessageArgs = null)
{
    public static ExecutionResult Ok(string messageKey, params string[] args) => new(true, messageKey, MessageArgs: args);

    public static ExecutionResult Background(string messageKey) => new(true, messageKey, StartedInBackground: true);

    public static ExecutionResult Fail(string messageKey, string? details = null) => new(false, messageKey, details);
}
