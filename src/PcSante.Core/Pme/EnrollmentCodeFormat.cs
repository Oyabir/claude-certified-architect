using System.Security.Cryptography;
using System.Text;

namespace PcSante.Core.Pme;

/// <summary>
/// Code d'inscription d'une organisation : PME-XXXXX-XXXXX-XXXXX (Crockford base32, 75 bits aléatoires,
/// sans I, L, O, U pour éviter les confusions à la saisie). Tirets et minuscules acceptés.
/// </summary>
public static class EnrollmentCodeFormat
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Groups = 3;
    private const int GroupLength = 5;

    public static string Generate()
    {
        var chars = new char[Groups * GroupLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Format(new string(chars));
    }

    /// <summary>Forme canonique, ou null si le texte n'est pas un code valide.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            return null;
        }

        var body = input.Trim().ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (body.StartsWith("PME", StringComparison.Ordinal))
        {
            body = body[3..];
        }

        return body.Length == Groups * GroupLength && body.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? Format(body) : null;
    }

    public static bool IsWellFormed(string? input) => Normalize(input) is not null;

    /// <summary>Empreinte stockée par la console (le code lui-même n'est jamais conservé).</summary>
    public static string Hash(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PcSante.Pme.Enrollment|" + normalized)));
    }

    private static string Format(string body) =>
        $"PME-{body[..GroupLength]}-{body[GroupLength..(2 * GroupLength)]}-{body[(2 * GroupLength)..]}";
}
