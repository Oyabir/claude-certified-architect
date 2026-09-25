using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>Sessions locales et RDP par les Services Bureau à distance (wtsapi32).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSessionApi : ISessionApi
{
    private const int MessageBoxInformation = 0x40;

    public Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<UserSession>>(() =>
    {
        if (!Wts.EnumerateSessions(Wts.CurrentServer, 0, 1, out var buffer, out var count))
        {
            return [];
        }

        var sessions = new List<UserSession>();
        try
        {
            var size = Marshal.SizeOf<Wts.SessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Wts.SessionInfo>(buffer + (i * size));
                if (info.State == Wts.StateListen || Read(info.SessionId) is not { } session)
                {
                    continue;
                }

                sessions.Add(session);
            }
        }
        finally
        {
            Wts.FreeMemory(buffer);
        }

        return sessions;
    }, cancellationToken);

    public Task<bool> SendMessageAsync(int sessionId, string title, string message, CancellationToken cancellationToken) =>
        Task.Run(() => Wts.SendMessage(Wts.CurrentServer, sessionId, title, title.Length * 2, message, message.Length * 2,
            MessageBoxInformation, 0, out _, wait: false), cancellationToken);

    public Task<bool> DisconnectAsync(int sessionId, CancellationToken cancellationToken) =>
        Task.Run(() => Wts.DisconnectSession(Wts.CurrentServer, sessionId, wait: true), cancellationToken);

    public Task<bool> LogOffAsync(int sessionId, CancellationToken cancellationToken) =>
        Task.Run(() => Wts.LogoffSession(Wts.CurrentServer, sessionId, wait: true), cancellationToken);

    /// <summary>Session d'un utilisateur ; null pour les sessions sans utilisateur (services, écoute).</summary>
    private static UserSession? Read(int sessionId)
    {
        var user = QueryString(sessionId, Wts.UserNameInfo);
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        var domain = QueryString(sessionId, Wts.DomainNameInfo);
        var remote = Query(sessionId, Wts.ClientProtocolTypeInfo, p => (int)(ushort)Marshal.ReadInt16(p)) == Wts.ProtocolRdp;
        var ex = Query(sessionId, Wts.SessionInfoEx, p => (Wts.InfoEx?)Marshal.PtrToStructure<Wts.InfoEx>(p));
        var state = ex is { } e
            ? MapState(e.SessionState, e.SessionFlags)
            : SessionState.Other;
        var logon = ex is { LogonTime: > 0 } l ? DateTimeOffset.FromFileTime(l.LogonTime) : (DateTimeOffset?)null;
        var address = remote ? Query(sessionId, Wts.ClientAddressInfo, p => ToAddress(Marshal.PtrToStructure<Wts.ClientAddress>(p))) : null;
        return new UserSession(sessionId, string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}", state, remote, address, logon);
    }

    /// <summary>État WTS (0 actif, 4 déconnecté) et verrouillage (drapeau 0 = verrouillée, Windows 8 et ultérieurs).</summary>
    internal static SessionState MapState(int wtsState, int flags) => wtsState switch
    {
        Wts.StateActive => flags == Wts.SessionFlagLocked ? SessionState.Locked : SessionState.Active,
        Wts.StateDisconnected => SessionState.Disconnected,
        _ => SessionState.Other,
    };

    /// <summary>Adresse IPv4 du client RDP (octets 2 à 5 pour AF_INET) ; null sinon.</summary>
    internal static string? ToAddress(Wts.ClientAddress address) =>
        address.AddressFamily == Wts.AddressFamilyInet && address.Address is { Length: >= 6 } bytes && bytes.Skip(2).Take(4).Any(b => b != 0)
            ? new IPAddress(bytes.AsSpan(2, 4)).ToString()
            : null;

    private static string? QueryString(int sessionId, int infoClass) => Query(sessionId, infoClass, Marshal.PtrToStringUni);

    private static T? Query<T>(int sessionId, int infoClass, Func<IntPtr, T?> read)
    {
        if (!Wts.QuerySessionInformation(Wts.CurrentServer, sessionId, infoClass, out var buffer, out _) || buffer == IntPtr.Zero)
        {
            return default;
        }

        try
        {
            return read(buffer);
        }
        finally
        {
            Wts.FreeMemory(buffer);
        }
    }
}
