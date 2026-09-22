using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PcSante.WindowsApi.Native;

/// <summary>
/// Suppression sûre pour un processus SYSTEM travaillant dans des dossiers modifiables par l'utilisateur :
/// le fichier est ouvert SANS suivre les points d'analyse, son chemin FINAL est contrôlé (il doit rester sous
/// la racine autorisée), puis il est supprimé via son handle. Une jonction ou un lien symbolique posé entre
/// l'énumération et la suppression ne peut donc pas rediriger la suppression vers un fichier système.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class SecureDelete
{
    private const uint Delete = 0x00010000;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint FlagBackupSemantics = 0x02000000;
    private const int FileDispositionInfoClass = 4;
    private const uint ReparsePointAttribute = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] buffer, uint size, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref FileDispositionInfo info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileBasicInfo info, uint size);

    /// <summary>Supprime le fichier (ou dossier vide) si et seulement si son chemin réel est sous <paramref name="allowedRoot"/>.</summary>
    public static bool TryDelete(string path, string allowedRoot, bool isDirectory)
    {
        var flags = FlagOpenReparsePoint | (isDirectory ? FlagBackupSemantics : 0);
        using var handle = CreateFile(@"\\?\" + path, Delete | FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        // Un point d'analyse (lien, jonction) n'est jamais supprimé ni traversé.
        if (!GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<FileBasicInfo>())
            || (basic.FileAttributes & ReparsePointAttribute) != 0)
        {
            return false;
        }

        var buffer = new char[1024];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
        {
            return false;
        }

        var finalPath = NormalizeFinal(new string(buffer, 0, (int)length));
        if (!IsUnder(finalPath, allowedRoot))
        {
            return false;
        }

        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        return SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disposition, (uint)Marshal.SizeOf<FileDispositionInfo>());
    }

    internal static string NormalizeFinal(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
        : path;

    internal static bool IsUnder(string path, string root)
    {
        var r = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }
}
