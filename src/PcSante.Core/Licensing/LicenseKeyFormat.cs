using System.Security.Cryptography;
using System.Text;

namespace PcSante.Core.Licensing;

/// <summary>
/// Format des clés : PCS-XXXXX-XXXXX-XXXXX-XXXXX (Crockford base32 sans I, L, O, U).
/// Le dernier caractère est une somme de contrôle qui détecte les fautes de frappe avant tout appel réseau.
/// </summary>
public static class LicenseKeyFormat
{
    public const string Prefix = "PCS";
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int BodyLength = 20;

    /// <summary>Génère une clé aléatoire (générateur cryptographique).</summary>
    public static string Generate()
    {
        Span<char> body = stackalloc char[BodyLength];
        for (var i = 0; i < BodyLength - 1; i++)
        {
            body[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        body[BodyLength - 1] = Checksum(body[..(BodyLength - 1)]);
        return Format(body);
    }

    /// <summary>Met en forme une saisie utilisateur (minuscules, espaces, confusions O/0, I/1, L/1).</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(BodyLength);
        var text = input.Trim().ToUpperInvariant();
        if (text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            text = text[Prefix.Length..];
        }

        foreach (var raw in text)
        {
            var c = raw switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => raw,
            };
            if (c is '-' or ' ')
            {
                continue;
            }

            if (Alphabet.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return null;
            }

            sb.Append(c);
        }

        if (sb.Length != BodyLength)
        {
            return null;
        }

        Span<char> body = stackalloc char[BodyLength];
        sb.CopyTo(0, body, BodyLength);
        return Checksum(body[..(BodyLength - 1)]) == body[BodyLength - 1] ? Format(body) : null;
    }

    public static bool IsWellFormed(string? input) => Normalize(input) is not null;

    /// <summary>Empreinte SHA-256 de la clé normalisée (stockage serveur, jeton).</summary>
    public static string Hash(string normalizedKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey)));

    /// <summary>Indice affichable (4 derniers caractères) : jamais la clé entière.</summary>
    public static string Hint(string normalizedKey) => "…" + normalizedKey[^4..];

    private static char Checksum(ReadOnlySpan<char> chars)
    {
        var sum = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            sum += (i + 1) * Alphabet.IndexOf(chars[i], StringComparison.Ordinal);
        }

        return Alphabet[sum % Alphabet.Length];
    }

    private static string Format(ReadOnlySpan<char> body) =>
        $"{Prefix}-{body[..5]}-{body[5..10]}-{body[10..15]}-{body[15..20]}";
}
