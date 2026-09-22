using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Ipc;
using PcSante.Licensing;

namespace PcSante.Service.Dispatch;

/// <summary>
/// Point d'entrée unique des demandes : validation stricte (catalogue fermé), contrôle de l'offre,
/// exécution (cycle complet pour les actions système), journal d'audit de chaque action et de chaque refus.
/// </summary>
public sealed partial class CommandDispatcher(
    CommandCatalog catalog,
    SystemActionPipeline pipeline,
    LicenseManager license,
    IAuditLog audit,
    TimeProvider time,
    ILogger<CommandDispatcher> logger) : IRequestHandler
{
    /// <summary>Une seule modification du système à la fois.</summary>
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private readonly ILogger<CommandDispatcher> _logger = logger;

    public Task<CommandResult> HandleAsync(IpcRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(client);
        var caller = new CallerIdentity(client.UserName, client.UserSid, client.ProcessId);
        return DispatchAsync(request.Command, request.Parameters, request.Confirmed, caller, cancellationToken);
    }

    /// <summary>Exécution interne (tâches planifiées) : mêmes contrôles que depuis l'interface.</summary>
    public async Task<CommandResult> DispatchAsync(
        string? command,
        IReadOnlyDictionary<string, string>? parameters,
        bool confirmed,
        CallerIdentity caller,
        CancellationToken cancellationToken,
        bool alreadyLocked = false)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var validation = CommandValidator.Validate(command, parameters, confirmed, SafePathExists);
        if (!validation.IsValid)
        {
            var messageKey = validation.Reason switch
            {
                FailureReason.NotInCatalog => "Result_NotInCatalog",
                FailureReason.ConfirmationRequired => "Result_ConfirmationRequired",
                _ => "Result_InvalidParameters",
            };
            LogRefused(command ?? "(vide)", validation.Reason, caller.UserName);
            await AuditRefusalAsync(caller, validation.Descriptor?.Id.ToString() ?? Sanitize(command), validation.Reason, validation.Details, cancellationToken).ConfigureAwait(false);
            return CommandResult.Refused(validation.Reason, messageKey, validation.Details);
        }

        var descriptor = validation.Descriptor!;
        if (descriptor.Tier == RequiredTier.Premium && !license.GetStatus().IsPremium)
        {
            await AuditRefusalAsync(caller, descriptor.Id.ToString(), FailureReason.LicenseRequired, null, cancellationToken).ConfigureAwait(false);
            return CommandResult.Refused(FailureReason.LicenseRequired, "Result_PremiumRequired");
        }

        var handler = catalog.Get(descriptor.Id);
        try
        {
            switch (handler)
            {
                case IQueryHandler query:
                    return await query.ExecuteAsync(validation.Parameters!, caller, cancellationToken).ConfigureAwait(false);

                case SystemAction action:
                    return await RunLockedAsync(alreadyLocked, () => pipeline.RunAsync(
                        action, new ActionContext(descriptor, validation.Parameters!, caller), cancellationToken), cancellationToken).ConfigureAwait(false);

                case IOperationHandler operation:
                    var result = operation.UsesActionLock
                        ? await RunLockedAsync(alreadyLocked, () => operation.ExecuteAsync(validation.Parameters!, caller, cancellationToken), cancellationToken).ConfigureAwait(false)
                        : await operation.ExecuteAsync(validation.Parameters!, caller, cancellationToken).ConfigureAwait(false);
                    if (!operation.SelfAudited)
                    {
                        await AuditOperationAsync(caller, descriptor.Id, validation.Parameters!, result, cancellationToken).ConfigureAwait(false);
                    }

                    return result;

                default:
                    throw new UnreachableException();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(ex, descriptor.Id);
            var failure = CommandResult.Failure(FailureReason.InternalError, "Result_InternalError", ex.Message);
            await AuditOperationAsync(caller, descriptor.Id, validation.Parameters!, failure, cancellationToken).ConfigureAwait(false);
            return failure;
        }
    }

    private async Task<CommandResult> RunLockedAsync(bool alreadyLocked, Func<Task<CommandResult>> run, CancellationToken cancellationToken)
    {
        if (alreadyLocked)
        {
            return await run().ConfigureAwait(false);
        }

        await _actionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await run().ConfigureAwait(false);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private Task AuditRefusalAsync(CallerIdentity caller, string command, FailureReason reason, string? details, CancellationToken ct) =>
        audit.WriteAsync(new AuditEntry
        {
            Timestamp = time.GetUtcNow(),
            Who = caller.UserName,
            ClientProcessId = caller.ProcessId,
            Command = command,
            Outcome = AuditOutcome.Refused,
            Reason = reason.ToString(),
            Details = details,
        }, ct);

    private Task AuditOperationAsync(CallerIdentity caller, CommandId id, CommandParameters parameters, CommandResult result, CancellationToken ct) =>
        audit.WriteAsync(new AuditEntry
        {
            Timestamp = time.GetUtcNow(),
            Who = caller.UserName,
            ClientProcessId = caller.ProcessId,
            Command = id.ToString(),
            Parameters = AuditFormatting.FormatParameters(parameters),
            Outcome = result.Status switch
            {
                CommandStatus.Succeeded => AuditOutcome.Succeeded,
                CommandStatus.AlreadyDone => AuditOutcome.AlreadyDone,
                CommandStatus.Started => AuditOutcome.Started,
                CommandStatus.Refused => AuditOutcome.Refused,
                _ => AuditOutcome.Failed,
            },
            Reason = result.Reason == FailureReason.None ? result.MessageKey : $"{result.Reason}:{result.MessageKey}",
            Details = result.TechnicalDetails,
        }, ct);

    private static bool SafePathExists(string path)
    {
        try
        {
            return Directory.Exists(path) || File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string Sanitize(string? command) =>
        command is null ? "(vide)" : new string(command.Take(64).Where(c => !char.IsControl(c)).ToArray());

    [LoggerMessage(Level = LogLevel.Warning, Message = "Commande refusée « {Command} » ({Reason}) pour {User}")]
    private partial void LogRefused(string command, FailureReason reason, string user);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur inattendue pendant {Command}")]
    private partial void LogUnexpected(Exception ex, CommandId command);
}
