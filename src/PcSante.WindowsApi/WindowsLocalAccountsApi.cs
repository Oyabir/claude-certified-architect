using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>
/// Comptes locaux par WMI (Win32_UserAccount). Le groupe Administrateurs est trouvé par son SID universel
/// (S-1-5-32-544), jamais par son nom, qui dépend de la langue de Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLocalAccountsApi : ILocalAccountsApi
{
    private const string Cimv2 = @"root\cimv2";
    private const string AdministratorsSid = "S-1-5-32-544";

    public Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<LocalAccount>>(() =>
    {
        var admins = AdministratorSids();
        return WmiHelper.Query(Cimv2, "SELECT Name, SID, Disabled FROM Win32_UserAccount WHERE LocalAccount = TRUE")
            .Select(o => (Name: WmiHelper.Get<string>(o, "Name"), Sid: WmiHelper.Get<string>(o, "SID"), Disabled: WmiHelper.Get<bool>(o, "Disabled")))
            .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Sid))
            .Select(a => new LocalAccount(a.Name!, a.Sid!, !a.Disabled, admins.Contains(a.Sid!)))
            .OrderByDescending(a => a.IsAdministrator).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }, cancellationToken);

    public Task<bool> SetEnabledAsync(string sid, bool enabled, CancellationToken cancellationToken) => Task.Run(() =>
    {
        // Le SID vient de l'énumération ci-dessus ; il est tout de même validé avant d'entrer dans une requête.
        var valid = new SecurityIdentifier(sid).Value;
        var account = WmiHelper.Query(Cimv2, $"SELECT * FROM Win32_UserAccount WHERE LocalAccount = TRUE AND SID = '{valid}'").FirstOrDefault();
        if (account is null)
        {
            return false;
        }

        account["Disabled"] = !enabled;
        account.Put();
        return true;
    }, cancellationToken);

    private static HashSet<string> AdministratorSids()
    {
        var group = WmiHelper.Query(Cimv2, $"SELECT Name, Domain FROM Win32_Group WHERE LocalAccount = TRUE AND SID = '{AdministratorsSid}'").FirstOrDefault();
        if (group is null)
        {
            return [];
        }

        var name = Escape(WmiHelper.Get<string>(group, "Name") ?? string.Empty);
        var domain = Escape(WmiHelper.Get<string>(group, "Domain") ?? Environment.MachineName);
        try
        {
            return WmiHelper.Query(Cimv2, $"ASSOCIATORS OF {{Win32_Group.Domain='{domain}',Name='{name}'}} WHERE AssocClass = Win32_GroupUser Role = GroupComponent")
                .Select(m => WmiHelper.Get<string>(m, "SID"))
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (ManagementException)
        {
            return [];
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
