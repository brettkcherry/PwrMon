using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PwrMon.Services;

/// <summary>
/// Authenticode verification for downloaded executables, via WinVerifyTrust — the same
/// check Explorer's "Digital Signatures" tab performs. Note that
/// <c>X509Certificate.CreateFromSignedFile</c> only *reads* the embedded certificate; it
/// validates neither the signature nor the chain, so it is not sufficient on its own.
/// </summary>
internal static class Authenticode
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeWholeChain = 1;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint RevocationCheckChain = 0x00000040;
    private const uint CacheOnlyUrlRetrieval = 0x00001000;

    /// <summary>
    /// Verifies <paramref name="path"/> carries a valid, trusted Authenticode signature.
    /// On success <paramref name="signer"/> is the signing certificate's subject — the
    /// caller still has to decide whether that signer is the one it expected.
    /// </summary>
    public static bool TryVerify(string path, out string signer, out string failureReason)
    {
        signer = "";
        failureReason = "";

        var result = VerifyTrust(path);
        if (result != 0)
        {
            failureReason = Describe(result);
            return false;
        }

        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            signer = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (signer.Length == 0) signer = cert.Subject;
        }
        catch (Exception ex)
        {
            // WinVerifyTrust already said the signature is good; failing to pretty-print the
            // subject shouldn't fail the whole check, but the caller should see something.
            Log.Error("read signer subject", ex);
            signer = "(unknown — signature is valid)";
        }
        return true;
    }

    private static int VerifyTrust(string path)
    {
        var fileInfo = new WintrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            pcwszFilePath = path,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, fDeleteOld: false);

            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeWholeChain,
                dwUnionChoice = ChoiceFile,
                pFile = pFile,
                dwStateAction = StateActionVerify,
                dwProvFlags = RevocationCheckChain | CacheOnlyUrlRetrieval,
            };

            var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustData>());
            try
            {
                Marshal.StructureToPtr(data, pData, fDeleteOld: false);
                var action = ActionGenericVerifyV2;
                var result = WinVerifyTrust(IntPtr.Zero, ref action, pData);

                // second pass frees the state data the verify pass allocated
                var done = Marshal.PtrToStructure<WintrustData>(pData);
                done.dwStateAction = StateActionClose;
                Marshal.StructureToPtr(done, pData, fDeleteOld: false);
                WinVerifyTrust(IntPtr.Zero, ref action, pData);

                return result;
            }
            finally { Marshal.FreeHGlobal(pData); }
        }
        catch (Exception ex)
        {
            Log.Error("WinVerifyTrust", ex);
            return unchecked((int)0x80004005); // E_FAIL — treat any failure to check as untrusted
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    private static string Describe(int hr) => (uint)hr switch
    {
        0x800B0100 => "the file is not signed",
        0x800B0101 => "the signing certificate has expired",
        0x800B0109 => "the signature chains to an untrusted root",
        0x800B010C => "the signing certificate was revoked",
        0x80096010 => "the file has been modified since it was signed",
        0x800B0111 => "the signer is explicitly distrusted on this machine",
        _ => $"signature check failed (0x{hr:X8})",
    };

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
