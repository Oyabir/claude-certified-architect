using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace PcSante.WindowsApi;

[SupportedOSPlatform("windows")]
internal static class WmiHelper
{
    public const string DefenderNamespace = @"root\Microsoft\Windows\Defender";
    public const string SecurityCenterNamespace = @"root\SecurityCenter2";

    public static List<ManagementObject> Query(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15) });
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    public static T? Get<T>(ManagementBaseObject o, string property)
    {
        try
        {
            var value = o[property];
            if (value is null)
            {
                return default;
            }

            return value is T t ? t : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or FormatException)
        {
            return default;
        }
    }

    public static DateTimeOffset? Date(ManagementBaseObject o, string property)
    {
        var value = o[property];
        return value switch
        {
            DateTime dt => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            string s when s.Length >= 14 => SafeDmtf(s),
            _ => null,
        };
    }

    private static DateTimeOffset? SafeDmtf(string dmtf)
    {
        try
        {
            var dt = ManagementDateTimeConverter.ToDateTime(dmtf);
            return dt.Year < 1990 ? null : new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Appelle une méthode statique WMI et renvoie ReturnValue (0 = succès).</summary>
    public static uint InvokeStatic(string scope, string className, string method, IReadOnlyDictionary<string, object> parameters)
    {
        using var cls = new ManagementClass(new ManagementScope(scope), new ManagementPath(className), null);
        using var inParams = cls.GetMethodParameters(method);
        foreach (var (name, value) in parameters)
        {
            inParams[name] = value;
        }

        using var result = cls.InvokeMethod(method, inParams, null);
        return result?["ReturnValue"] is { } rv ? Convert.ToUInt32(rv, CultureInfo.InvariantCulture) : 0;
    }
}
