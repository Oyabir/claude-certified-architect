using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcSante.WindowsApi.Native;

/// <summary>Services Bureau à distance (wtsapi32) : sessions locales et RDP du PC courant.</summary>
[SupportedOSPlatform("windows")]
internal static partial class Wts
{
    internal static readonly IntPtr CurrentServer = IntPtr.Zero;

    internal const int UserNameInfo = 5;
    internal const int DomainNameInfo = 7;
    internal const int ClientAddressInfo = 14;
    internal const int ClientProtocolTypeInfo = 16;
    internal const int SessionInfoEx = 25;

    internal const int StateActive = 0;
    internal const int StateDisconnected = 4;
    internal const int StateListen = 6;
    internal const int SessionFlagLocked = 0;
    internal const int ProtocolRdp = 2;
    internal const int AddressFamilyInet = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ClientAddress
    {
        public int AddressFamily;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Address;
    }

    /// <summary>WTSINFOEXW niveau 1 (Windows 8 et ultérieurs).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InfoEx
    {
        public int Level;
        public int SessionId;
        public int SessionState;
        public int SessionFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
        public string WinStationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
        public string UserName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)]
        public string DomainName;

        public long LogonTime;
        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long CurrentTime;
        public int IncomingBytes;
        public int OutgoingBytes;
        public int IncomingFrames;
        public int OutgoingFrames;
        public int IncomingCompressedBytes;
        public int OutgoingCompressedBytes;
    }

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory")]
    internal static partial void FreeMemory(IntPtr memory);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSDisconnectSession", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DisconnectSession(IntPtr server, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSLogoffSession", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LogoffSession(IntPtr server, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSSendMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendMessage(IntPtr server, int sessionId, string title, int titleLength, string message, int messageLength,
        int style, int timeout, out int response, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
