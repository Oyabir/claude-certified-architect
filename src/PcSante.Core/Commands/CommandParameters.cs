using System.Globalization;

namespace PcSante.Core.Commands;

/// <summary>Paramètres d'une commande, uniquement construits après validation stricte.</summary>
public sealed class CommandParameters
{
    public static CommandParameters Empty { get; } = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _values;

    internal CommandParameters(IReadOnlyDictionary<string, string> values) => _values = values;

    public IReadOnlyDictionary<string, string> Values => _values;

    public string GetString(string name) =>
        _values.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException(name);

    public string? GetOptionalString(string name) => _values.GetValueOrDefault(name);

    public int GetInt(string name) => int.Parse(GetString(name), NumberStyles.None, CultureInfo.InvariantCulture);

    public bool GetBool(string name) => bool.Parse(GetString(name));

    public Guid GetGuid(string name) => Guid.Parse(GetString(name));

    public TimeOnly GetTime(string name) => TimeOnly.ParseExact(GetString(name), "HH:mm", CultureInfo.InvariantCulture);

    public TEnum GetEnum<TEnum>(string name)
        where TEnum : struct, Enum => Enum.Parse<TEnum>(GetString(name), ignoreCase: false);
}
