using System.Management;
using System.Runtime.Versioning;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>
/// BitLocker par l'interface WMI officielle (Win32_EncryptableVolume, Win32_Tpm), sans manage-bde ni PowerShell.
/// Disque système uniquement ; aucune fonction sur Windows Famille.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBitLockerApi(ISystemInfoApi system) : IBitLockerApi
{
    private const string VolumeNamespace = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string TpmNamespace = @"root\CIMV2\Security\MicrosoftTpm";
    private const uint NumericalPassword = 3;
    private const uint Tpm = 1;
    private const uint UsedSpaceOnly = 1;

    public Task<BitLockerStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (system.GetSystemInfo().IsHomeEdition || SystemVolume() is not { } volume)
        {
            return BitLockerStatus.NotSupported;
        }

        using (volume)
        {
            var conversion = Invoke(volume, "GetConversionStatus");
            var state = MapState(Out<uint>(conversion, "ConversionStatus"), Out<uint>(Invoke(volume, "GetProtectionStatus"), "ProtectionStatus"));
            return new BitLockerStatus(true, TpmReady(), state, (int)Out<uint>(conversion, "EncryptionPercentage"),
                ProtectorIds(volume, NumericalPassword).Length > 0);
        }
    }, cancellationToken);

    public Task<BitLockerRecoveryKey?> EnsureRecoveryKeyAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var volume = SystemVolume();
        if (volume is null)
        {
            return null;
        }

        var id = ProtectorIds(volume, NumericalPassword).FirstOrDefault();
        if (id is null)
        {
            // Sans mot de passe fourni, Windows génère lui-même une clé de 48 chiffres.
            var created = Invoke(volume, "ProtectKeyWithNumericalPassword");
            id = ReturnValue(created) == 0 ? Out<string>(created, "VolumeKeyProtectorID") : null;
        }

        if (id is null)
        {
            return null;
        }

        var password = Invoke(volume, "GetKeyProtectorNumericalPassword", ("VolumeKeyProtectorID", id));
        return ReturnValue(password) == 0 && Out<string>(password, "NumericalPassword") is { Length: > 0 } key
            ? new BitLockerRecoveryKey(id, key)
            : null;
    }, cancellationToken);

    public Task<bool> StartEncryptionAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var volume = SystemVolume();
        if (volume is null || ProtectorIds(volume, NumericalPassword).Length == 0)
        {
            return false;
        }

        if (ProtectorIds(volume, Tpm).Length == 0 && ReturnValue(Invoke(volume, "ProtectKeyWithTPM")) != 0)
        {
            return false;
        }

        return ReturnValue(Invoke(volume, "Encrypt", ("EncryptionMethod", 0u), ("EncryptionFlags", UsedSpaceOnly))) == 0;
    }, cancellationToken);

    /// <summary>Conversion (0 déchiffré, 1 chiffré, 2 chiffrement, 3 déchiffrement, 4-5 en pause) et protection (1 = active).</summary>
    internal static BitLockerState MapState(uint conversion, uint protection) => conversion switch
    {
        0 => BitLockerState.Off,
        1 => protection == 1 ? BitLockerState.On : BitLockerState.Paused,
        2 => BitLockerState.Encrypting,
        3 => BitLockerState.Decrypting,
        4 or 5 => BitLockerState.Paused,
        _ => BitLockerState.Unknown,
    };

    private static ManagementObject? SystemVolume()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        try
        {
            return WmiHelper.Query(VolumeNamespace, $"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'").FirstOrDefault();
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static bool TpmReady()
    {
        try
        {
            using var tpm = WmiHelper.Query(TpmNamespace, "SELECT * FROM Win32_Tpm").FirstOrDefault();
            return tpm is not null && Out<bool>(Invoke(tpm, "IsReady"), "IsReady");
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    private static string[] ProtectorIds(ManagementObject volume, uint type) =>
        Out<string[]>(Invoke(volume, "GetKeyProtectors", ("KeyProtectorType", type)), "VolumeKeyProtectorID") ?? [];

    private static ManagementBaseObject Invoke(ManagementObject target, string method, params (string Name, object Value)[] arguments)
    {
        var input = arguments.Length == 0 ? null : target.GetMethodParameters(method);
        foreach (var (name, value) in arguments)
        {
            input![name] = value;
        }

        return target.InvokeMethod(method, input, null);
    }

    private static uint ReturnValue(ManagementBaseObject result) => Out<uint>(result, "ReturnValue");

    private static T? Out<T>(ManagementBaseObject result, string name) => WmiHelper.Get<T>(result, name);
}
