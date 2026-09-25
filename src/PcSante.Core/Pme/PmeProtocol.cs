using PcSante.Core.Health;

namespace PcSante.Core.Pme;

/// <summary>
/// Protocole entre le service Windows d'un poste et la console PME (V2). Seules des métriques techniques
/// circulent (score, problèmes, version de Windows) : aucune donnée personnelle (loi 09-08).
/// </summary>
public static class PmeProtocol
{
    public const string EnrollPath = "api/v1/devices/enroll";
    public const string ReportPath = "api/v1/devices/report";

    /// <summary>En-tête d'authentification d'un poste : « identifiant:secret ».</summary>
    public const string DeviceHeader = "X-PcSante-Device";

    public const int MaxIssues = 50;
    public const int MaxArgs = 5;
    public const int MaxTextLength = 128;
}

public enum PmeError
{
    None,
    InvalidRequest,
    InvalidCode,
    NoSeatLeft,
    Unauthorized,
    Revoked,
}

/// <param name="EnrollmentCode">Code d'inscription de l'organisation (donné par le gérant).</param>
public sealed record EnrollRequest(string EnrollmentCode, string MachineName, string AppVersion);

/// <summary>Réponse d'inscription : le secret n'est transmis qu'une fois, le poste le garde chiffré (DPAPI).</summary>
public sealed record EnrollResponse(PmeError Error, Guid DeviceId, string? Secret, string? OrganizationName)
{
    public bool Ok => Error == PmeError.None;

    public static EnrollResponse Fail(PmeError error) => new(error, Guid.Empty, null, null);
}

public sealed record ReportedIssue(string Code, IssueSeverity Severity, IReadOnlyList<string> Args);

public sealed record DeviceReport(
    DateTimeOffset AnalyzedAt,
    int Score,
    SubScores SubScores,
    IReadOnlyList<ReportedIssue> Issues,
    string WindowsVersion,
    string AppVersion)
{
    public static DeviceReport From(HealthReport report, string windowsVersion, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DeviceReport(report.AnalyzedAt, report.Score, report.SubScores,
            report.Issues.Take(PmeProtocol.MaxIssues).Select(i => new ReportedIssue(i.Code, i.Severity, i.Args.Take(PmeProtocol.MaxArgs).ToList())).ToList(),
            windowsVersion, appVersion);
    }
}

public sealed record PmeResponse(PmeError Error)
{
    public bool Ok => Error == PmeError.None;
}
