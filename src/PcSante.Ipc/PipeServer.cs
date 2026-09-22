using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using PcSante.Core.Commands;
using PcSante.Ipc.Security;

namespace PcSante.Ipc;

/// <summary>
/// Serveur du named pipe. Pour chaque connexion : vérification du client, puis boucle demande/réponse.
/// Un client non vérifié reçoit un refus et la connexion est fermée sans traiter sa demande.
/// </summary>
public sealed partial class PipeServer(
    string pipeName,
    IPipeStreamFactory streamFactory,
    IClientVerifier verifier,
    IRequestHandler handler,
    ILogger<PipeServer> logger,
    int maxInstances = 4)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<PipeServer> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var first = true;
        var connections = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = streamFactory.Create(pipeName, maxInstances, first);
                first = false;
            }
            catch (IOException ex)
            {
                LogCreateFailed(ex);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (IOException ex)
            {
                LogConnectionFailed(ex);
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            connections.RemoveAll(t => t.IsCompleted);
            connections.Add(Task.Run(() => ServeAsync(pipe, cancellationToken), CancellationToken.None));
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
    }

    internal async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var _ = pipe.ConfigureAwait(false);
        try
        {
            var verification = verifier.Verify(pipe);
            if (!verification.IsTrusted || verification.Client is null)
            {
                LogClientRefused(verification.Decision, verification.ExecutablePath ?? "?");
                await RefuseAsync(pipe, cancellationToken).ConfigureAwait(false);
                return;
            }

            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idle.CancelAfter(IdleTimeout);
                IpcRequest? request;
                try
                {
                    request = await MessageFraming.ReadAsync<IpcRequest>(pipe, MessageFraming.MaxRequestBytes, idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (request is null)
                {
                    return;
                }

                CommandResult result;
                if (request.Version != IpcRequest.CurrentVersion || string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 64)
                {
                    result = CommandResult.Refused(FailureReason.InvalidParameters, "Result_InvalidRequest");
                }
                else
                {
                    result = await handler.HandleAsync(request, verification.Client, cancellationToken).ConfigureAwait(false);
                }

                await MessageFraming.WriteAsync(pipe, new IpcResponse { RequestId = request.RequestId ?? string.Empty, Result = result },
                    MessageFraming.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException)
        {
            // Message mal formé ou client déconnecté : on ferme la connexion sans rien exécuter.
            LogProtocolError(ex.GetType().Name);
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service.
        }
    }

    private static async Task RefuseAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            await MessageFraming.WriteAsync(pipe, new IpcResponse
            {
                RequestId = string.Empty,
                Result = CommandResult.Refused(FailureReason.ClientNotTrusted, "Result_ClientNotTrusted"),
            }, MessageFraming.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Client déjà parti.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Client du named pipe refusé ({Decision}) : {Path}")]
    private partial void LogClientRefused(TrustDecision decision, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erreur de protocole sur le named pipe : {Error}")]
    private partial void LogProtocolError(string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Création du named pipe impossible")]
    private partial void LogCreateFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connexion au named pipe échouée")]
    private partial void LogConnectionFailed(Exception ex);
}
