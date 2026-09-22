using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcSante.WindowsApi.Native;

/// <summary>P/Invoke WinVerifyTrust et catalogues de sécurité (fichiers système signés par catalogue).</summary>
[SupportedOSPlatform("windows")]
internal static partial class WinTrust
{
    internal static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    internal const uint UiNone = 2;
    internal const uint RevokeNone = 0;
    internal const uint ChoiceFile = 1;
    internal const uint ChoiceCatalog = 2;
    internal const uint StateActionVerify = 1;
    internal const uint StateActionClose = 2;
    internal const uint CacheOnlyUrlRetrieval = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CatalogInfo
    {
        public uint StructSize;
        public uint CatalogVersion;
        public IntPtr CatalogFilePath;
        public IntPtr MemberTag;
        public IntPtr MemberFilePath;
        public IntPtr MemberFile;
        public IntPtr CalculatedFileHash;
        public uint CalculatedFileHashSize;
        public IntPtr CatalogContext;
        public IntPtr CatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr Info;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CatalogInfoResult
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CatalogFile;
    }

    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptCATAdminAcquireContext2(out IntPtr catAdmin, IntPtr subsystem, [MarshalAs(UnmanagedType.LPWStr)] string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptCATAdminCalcHashFromFileHandle2(IntPtr catAdmin, IntPtr file, ref uint hashSize, [Out] byte[]? hash, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    internal static partial IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr catAdmin, [In] byte[] hash, uint hashSize, uint flags, IntPtr previousCatInfo);

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CatalogInfoResult result, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);
}
