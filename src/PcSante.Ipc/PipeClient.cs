using System.Globalization;
using System.IO.Pipes;
using PcSante.Core.Commands;

namespace PcSante.Ipc;

/// <summary>Vérifie que le serveur du pipe est bien le service (anti-usurpation), côté client.</summary>
public interface IServerVerifier
{
    bool IsGenuineService(NamedPipeClientStream pipe);
}

public sealed class AcceptAnyServer : IServerVerifier
{
    public bool IsGenuineService(NamedPipeClientStream pipe) => true;
}

/// <summary>
/// Client du service, utilisé par l'interface et le mini-affichage. Une seule demande à la fois ;
/// reconnexion automatique. En cas d'indisponibilité, renvoie un résultat « ServiceUnavailable » clair.
/// </summary>
public sealed class PipeClient(string pipeName, IServerVerifier serverVerifier, TimeSpan? connectTimeout = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    private NamedPipeClientStream? _pipe;
    private long _counter;

    public async Task<CommandResult> SendAsync(
        CommandId command,
        IReadOnlyDictionary<string, string>? parameters = null,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture),
            Command = command.ToString(),
            Parameters = parameters?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Confirmed = confirmed,
        };
        return await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Envoi brut (utilisé par les tests de sécurité pour simuler un client malveillant).</summary>
    public async Task<CommandResult> SendRawAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var pipe = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                    if (pipe is null)
                    {
                        return Unavailable();
                    }

                    await MessageFraming.WriteAsync(pipe, request, MessageFraming.MaxRequestBytes, cancellationToken).ConfigureAwait(false);
                    var response = await MessageFraming.ReadAsync<IpcResponse>(pipe, MessageFraming.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                    if (response is not null)
                    {
                        if (response.Result.Reason == FailureReason.ClientNotTrusted)
                        {
                            await ResetAsync().ConfigureAwait(false);
                        }

                        return response.Result;
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or InvalidDataException)
                {
                    // Connexion perdue (service redémarré) : une nouvelle tentative.
                }

                await ResetAsync().ConfigureAwait(false);
            }

            return Unavailable();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    private async Task<NamedPipeClientStream?> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_pipe is { IsConnected: true })
        {
            return _pipe;
        }

        await ResetAsync().ConfigureAwait(false);
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectTimeout);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        if (!serverVerifier.IsGenuineService(pipe))
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        _pipe = pipe;
        return pipe;
    }

    private async Task ResetAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }
    }

    private static CommandResult Unavailable() =>
        CommandResult.Failure(FailureReason.ServiceUnavailable, "Result_ServiceUnavailable");
}
