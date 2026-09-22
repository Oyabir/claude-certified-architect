using System.Text.Json;
using PcSante.Core;
using PcSante.Core.Actions;
using PcSante.Core.Commands;

namespace PcSante.Service.Actions;

internal static class Backup
{
    public static BackupData Of<T>(string description, T state) =>
        new(description, JsonSerializer.Serialize(state, PcSanteJson.Options));

    public static T Read<T>(BackupData backup) =>
        JsonSerializer.Deserialize<T>(backup.Payload, PcSanteJson.Options) ?? throw new InvalidDataException("Sauvegarde illisible.");
}

internal static class Checks
{
    public static CheckResult NotAvailable(string messageKey) =>
        CheckResult.Blocked(FailureReason.NotSupportedOnThisPc, messageKey);
}
