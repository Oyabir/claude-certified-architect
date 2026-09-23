using System.Buffers.Binary;
using System.Text.Json;
using PcSante.Core;

namespace PcSante.Ipc;

/// <summary>
/// Encadrement des messages : longueur sur 4 octets (little-endian) puis JSON UTF-8.
/// Les tailles sont bornées pour empêcher un client d'épuiser la mémoire du service.
/// </summary>
public static class MessageFraming
{
    public const int MaxRequestBytes = 64 * 1024;
    public const int MaxResponseBytes = 8 * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, PcSanteJson.Options);
        if (payload.Length > maxBytes)
        {
            throw new InvalidDataException("Message trop volumineux.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <returns>Le message, ou default si le flux est fermé proprement avant un nouvel en-tête.</returns>
    public static async Task<T?> ReadAsync<T>(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, allowEof: true, cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes)
        {
            throw new InvalidDataException("Taille de message invalide.");
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, allowEof: false, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, PcSanteJson.Options);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, bool allowEof, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0 && allowEof)
                {
                    return false;
                }

                throw new EndOfStreamException("Connexion interrompue.");
            }

            read += n;
        }

        return true;
    }
}
