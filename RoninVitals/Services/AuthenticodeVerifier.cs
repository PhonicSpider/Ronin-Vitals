using System.Runtime.InteropServices;

namespace RoninVitals.Services;

/// <summary>
/// Checks a file's digital signature with Windows' own trust provider (the same one behind Explorer's
/// Properties > Digital Signatures tab). Revocation checks are off and URL retrieval is cache-only, so it never
/// touches the network.
/// </summary>
internal static class AuthenticodeVerifier
{
    /// <summary>Result code meaning "signature is valid".</summary>
    public const uint ValidSignature = 0;

    /// <summary>Returned when the check itself could not run, so the file counts as not verified.</summary>
    public const uint CheckNotRun = uint.MaxValue;

    private const uint NoUserInterface = 2;
    private const uint VerifyFile = 1;
    private const uint NoRevocationCheck = 0;
    private const uint CacheOnlyUrlRetrieval = 0x1000;

    private static Guid _genericVerifyAction = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2

    // The field order must match the Win32 structs WINTRUST_FILE_INFO and WINTRUST_DATA; the names are descriptive.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr windowHandle, ref Guid action, ref WinTrustData data);

    /// <summary>0 for a valid signature; otherwise a Windows trust error code (for example 0x800B0100 = not signed).</summary>
    public static uint Verify(string filePath)
    {
        var fileInfo = new WinTrustFileInfo { FilePath = filePath };
        fileInfo.StructSize = (uint)Marshal.SizeOf(fileInfo);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var data = new WinTrustData
            {
                UiChoice = NoUserInterface,
                RevocationChecks = NoRevocationCheck,
                UnionChoice = VerifyFile,
                FileInfo = fileInfoPointer,
                ProviderFlags = CacheOnlyUrlRetrieval,
            };
            data.StructSize = (uint)Marshal.SizeOf(data);

            const int invalidHandleValue = -1; // as the window handle: never show UI
            return WinVerifyTrust(new IntPtr(invalidHandleValue), ref _genericVerifyAction, ref data);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return CheckNotRun;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }
}
