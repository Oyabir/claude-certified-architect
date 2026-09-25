using Microsoft.Extensions.Logging;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Pme;
using PcSante.Core.Windows;

namespace PcSante.Service.Pme;

/// <summary>
/// Poste rattaché à une console PME : inscription, détachement, envoi du dernier rapport de santé
/// (après chaque analyse et toutes les 6 heures). Métriques techniques uniquement.
/// </summary>
/// <param name="consoleUrl">Adresse de la console (branding.props) ; vide = fonction masquée.</param>
public sealed partial class PmeReporter(IPmeClient client, PmeEnrollmentStore store, ISystemInfoApi system, TimeProvider time, ILogger<PmeReporter> logger, string consoleUrl)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly ILogger<PmeReporter> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsAvailable { get; } = Uri.TryCreate(consoleUrl, UriKind.Absolute, out _);

    public PmeStatus Status()
    {
        var enrollment = store.Load();
        return new PmeStatus(IsAvailable, enrollment is not null, enrollment?.OrganizationName, enrollment?.LastReportAt, enrollment?.LastError ?? PmeError.None);
    }

    public async Task<CommandResult> EnrollAsync(string code, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return CommandResult.Refused(FailureReason.NotSupportedOnThisPc, "Result_PmeNotConfigured");
        }

        var info = system.GetSystemInfo();
        var response = await client.EnrollAsync(new EnrollRequest(code, info.MachineName, ProductInfo.Version), ct).ConfigureAwait(false);
        if (response is null)
        {
            return CommandResult.Failure(FailureReason.NetworkUnavailable, "Result_PmeUnreachable");
        }

        if (!response.Ok || response.Secret is null)
        {
            return CommandResult.Refused(FailureReason.PreconditionFailed, $"Result_Pme{response.Error}");
        }

        store.Save(new PmeEnrollment(response.DeviceId, response.Secret, response.OrganizationName ?? string.Empty, time.GetUtcNow(), null, PmeError.None));
        return CommandResult.Success("Result_PmeEnrolled", response.OrganizationName ?? string.Empty);
    }

    public CommandResult Leave()
    {
        if (store.Load() is null)
        {
            return new CommandResult { Status = CommandStatus.AlreadyDone, MessageKey = "Result_PmeNotEnrolled" };
        }

        store.Delete();
        return CommandResult.Success("Result_PmeLeft");
    }

    /// <summary>Envoie un rapport si le poste est rattaché ; sans effet sinon. Ne lève jamais d'exception.</summary>
    public async Task SendAsync(HealthReport? report, CancellationToken ct)
    {
        if (report is null || !IsAvailable || store.Load() is not { LastError: not PmeError.Revoked } enrollment)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var info = system.GetSystemInfo();
            var payload = DeviceReport.From(report, $"{info.ProductName} {info.DisplayVersion}".Trim(), ProductInfo.Version);
            var response = await client.ReportAsync(enrollment.DeviceId, enrollment.Secret, payload, ct).ConfigureAwait(false);
            if (response is null)
            {
                return;
            }

            store.Save(enrollment with { LastReportAt = response.Ok ? time.GetUtcNow() : enrollment.LastReportAt, LastError = response.Error });
            if (!response.Ok)
            {
                LogRefused(response.Error);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStoreFailed(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Console PME : rapport refusé ({Error})")]
    private partial void LogRefused(PmeError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Console PME : inscription illisible ou non enregistrable")]
    private partial void LogStoreFailed(Exception ex);
}
