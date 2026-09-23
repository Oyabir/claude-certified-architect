using System.IO;
using System.Runtime.Versioning;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Ipc;
using PcSante.Ipc.Windows;

namespace PcSante.App.Infrastructure;

/// <summary>
/// Accès au service. L'interface n'a aucun privilège : elle envoie des demandes du catalogue et affiche les résultats.
/// Deux connexions : une pour les lectures rapides, une pour les actions longues (SFC, mises à jour).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceClient : IAsyncDisposable
{
    private readonly PipeClient _quick;
    private readonly PipeClient _long;

    public ServiceClient()
    {
        var serviceExe = Path.Combine(AppContext.BaseDirectory, "PcSante.Service.exe");
        _quick = new PipeClient(ProductInfo.PipeName, new WindowsServerVerifier(serviceExe));
        _long = new PipeClient(ProductInfo.PipeName, new WindowsServerVerifier(serviceExe));
    }

    public Task<CommandResult> RunAsync(CommandId command, IReadOnlyDictionary<string, string>? parameters = null, bool confirmed = false)
    {
        var descriptor = CommandDefinitions.Get(command);
        var client = descriptor.Kind == CommandKind.Action ? _long : _quick;
        return client.SendAsync(command, parameters, confirmed);
    }

    public async Task<T?> QueryAsync<T>(CommandId command, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var result = await _quick.SendAsync(command, parameters).ConfigureAwait(true);
        return result.IsSuccess ? result.GetData<T>() : default;
    }

    public async ValueTask DisposeAsync()
    {
        await _quick.DisposeAsync().ConfigureAwait(false);
        await _long.DisposeAsync().ConfigureAwait(false);
    }
}
