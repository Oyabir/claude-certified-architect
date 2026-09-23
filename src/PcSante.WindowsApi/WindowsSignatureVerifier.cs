using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>
/// Vérification Authenticode (WinVerifyTrust) : signature intégrée au fichier, sinon signature par catalogue
/// (cas de la plupart des fichiers système Windows). Pas de vérification de révocation en ligne (rapidité, hors ligne).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSignatureVerifier : ISignatureVerifier
{
    public SignatureInfo Verify(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return SignatureInfo.Unsigned;
        }

        if (VerifyEmbedded(filePath))
        {
            var (publisher, thumbprint) = ReadSigner(filePath);
            return new SignatureInfo(true, true, publisher, thumbprint);
        }

        var catalog = FindCatalog(filePath);
        if (catalog is not null && VerifyCatalogMember(filePath, catalog))
        {
            var (publisher, thumbprint) = ReadSigner(catalog.CatalogFile);
            return new SignatureInfo(true, true, publisher, thumbprint);
        }

        return SignatureInfo.Unsigned;
    }

    private static bool VerifyEmbedded(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = new WinTrust.FileInfo { StructSize = (uint)Marshal.SizeOf<WinTrust.FileInfo>(), FilePath = pathPtr };
        var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrust.FileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, infoPtr, false);
            return Run(WinTrust.ChoiceFile, infoPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    private static bool VerifyCatalogMember(string path, CatalogMatch match)
    {
        var catPtr = Marshal.StringToHGlobalUni(match.CatalogFile);
        var memberPtr = Marshal.StringToHGlobalUni(path);
        var tagPtr = Marshal.StringToHGlobalUni(match.MemberTag);
        var hashPtr = Marshal.AllocHGlobal(match.Hash.Length);
        var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrust.CatalogInfo>());
        try
        {
            Marshal.Copy(match.Hash, 0, hashPtr, match.Hash.Length);
            var info = new WinTrust.CatalogInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrust.CatalogInfo>(),
                CatalogFilePath = catPtr,
                MemberTag = tagPtr,
                MemberFilePath = memberPtr,
                CalculatedFileHash = hashPtr,
                CalculatedFileHashSize = (uint)match.Hash.Length,
            };
            Marshal.StructureToPtr(info, infoPtr, false);
            return Run(WinTrust.ChoiceCatalog, infoPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
            Marshal.FreeHGlobal(hashPtr);
            Marshal.FreeHGlobal(tagPtr);
            Marshal.FreeHGlobal(memberPtr);
            Marshal.FreeHGlobal(catPtr);
        }
    }

    private static bool Run(uint choice, IntPtr info)
    {
        var action = WinTrust.GenericVerifyV2;
        var data = new WinTrust.TrustData
        {
            StructSize = (uint)Marshal.SizeOf<WinTrust.TrustData>(),
            UiChoice = WinTrust.UiNone,
            RevocationChecks = WinTrust.RevokeNone,
            UnionChoice = choice,
            Info = info,
            StateAction = WinTrust.StateActionVerify,
            ProvFlags = WinTrust.CacheOnlyUrlRetrieval,
        };
        var result = WinTrust.WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        data.StateAction = WinTrust.StateActionClose;
        WinTrust.WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        return result == 0;
    }

    private sealed record CatalogMatch(string CatalogFile, string MemberTag, byte[] Hash);

    private static CatalogMatch? FindCatalog(string path)
    {
        if (!WinTrust.CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var handle = stream.SafeFileHandle.DangerousGetHandle();
            uint size = 0;
            WinTrust.CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, null, 0);
            if (size == 0 || size > 1024)
            {
                return null;
            }

            var hash = new byte[size];
            if (!WinTrust.CryptCATAdminCalcHashFromFileHandle2(admin, handle, ref size, hash, 0))
            {
                return null;
            }

            var catInfo = WinTrust.CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var result = new WinTrust.CatalogInfoResult { StructSize = (uint)Marshal.SizeOf<WinTrust.CatalogInfoResult>() };
                return WinTrust.CryptCATCatalogInfoFromContext(catInfo, ref result, 0)
                    ? new CatalogMatch(result.CatalogFile, Convert.ToHexString(hash), hash)
                    : null;
            }
            finally
            {
                WinTrust.CryptCATAdminReleaseCatalogContext(admin, catInfo, 0);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            WinTrust.CryptCATAdminReleaseContext(admin, 0);
        }
    }

    private static (string? Publisher, string? Thumbprint) ReadSigner(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // API disponible et adaptée en .NET 8 pour lire le signataire Authenticode.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return (cert.GetNameInfo(X509NameType.SimpleName, false), cert.Thumbprint);
        }
        catch (CryptographicException)
        {
            return (null, null);
        }
    }
}
