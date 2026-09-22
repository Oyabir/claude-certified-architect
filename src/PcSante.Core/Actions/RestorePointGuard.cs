using PcSante.Core.Windows;

namespace PcSante.Core.Actions;

/// <summary>
/// Garantit qu'un point de restauration récent existe avant une modification.
/// Windows n'accepte qu'un point par 24 h par défaut : un point créé il y a moins de
/// <see cref="ReuseWindow"/> par PC Santé est considéré comme valable.
/// </summary>
public sealed class RestorePointGuard(IRestorePointApi api, TimeProvider time)
{
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(30);

    public async Task<bool> EnsureAsync(string description, CancellationToken cancellationToken)
    {
        if (!await api.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var now = time.GetUtcNow();
        var existing = await api.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(p => now - p.CreatedAt < ReuseWindow))
        {
            return true;
        }

        if (await api.CreateAsync(description, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        // Windows peut ignorer silencieusement la création (fréquence limitée) : on contrôle la liste.
        existing = await api.ListAsync(cancellationToken).ConfigureAwait(false);
        return existing.Any(p => now - p.CreatedAt < ReuseWindow);
    }
}
