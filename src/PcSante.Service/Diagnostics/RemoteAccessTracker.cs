using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Windows;
using PcSante.Service.Data;

namespace PcSante.Service.Diagnostics;

/// <summary>
/// Adresses des connexions Bureau à distance (RDP) déjà vues sur ce PC (M7). Une adresse vue pour la première
/// fois depuis moins de 24 heures est « inhabituelle » : l'analyse la signale. Aucune donnée ne quitte le PC.
/// </summary>
public sealed class RemoteAccessTracker(HistoryStore history, TimeProvider time)
{
    public static readonly TimeSpan NewAddressWindow = TimeSpan.FromHours(24);
    private const string Key = "rdp.addresses";
    private const int MaxAddresses = 500;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Enregistre les adresses des sessions RDP et renvoie celles vues pour la première fois récemment.</summary>
    public async Task<IReadOnlyList<string>> RecordAsync(IEnumerable<UserSession> sessions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = time.GetUtcNow();
            var seen = await LoadAsync(ct).ConfigureAwait(false);
            var changed = false;
            foreach (var address in sessions.Where(s => s.IsRemote).Select(s => s.ClientAddress).OfType<string>())
            {
                changed |= seen.TryAdd(address, now);
            }

            if (changed)
            {
                var kept = seen.OrderByDescending(p => p.Value).Take(MaxAddresses).ToDictionary(p => p.Key, p => p.Value);
                await history.SetValueAsync(Key, JsonSerializer.Serialize(kept, PcSanteJson.Options), ct).ConfigureAwait(false);
                seen = kept;
            }

            return seen.Where(p => now - p.Value < NewAddressWindow).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, DateTimeOffset>> LoadAsync(CancellationToken ct)
    {
        var json = await history.GetValueAsync(Key, ct).ConfigureAwait(false);
        try
        {
            return json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json, PcSanteJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
