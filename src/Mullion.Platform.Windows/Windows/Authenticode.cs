using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Who signed an executable, if anyone did and the signature is actually valid.
/// <para>
/// Used by the self-updater to refuse a replacement less trustworthy than what
/// is already installed. It answers one question - "is this file validly signed,
/// and by whom" - because that is the only one the comparison needs.
/// </para>
/// </summary>
public static class Authenticode
{
    /// <summary>
    /// The subject of the certificate that signed the file, or null when it is
    /// unsigned, the signature does not verify, or the chain cannot be trusted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves matter and neither is sufficient alone.
    /// <see cref="X509Certificate.CreateFromSignedFile"/> reads the embedded
    /// certificate but does NOT check that it signs this file's contents - it
    /// returns a perfectly good certificate for a binary someone has since
    /// edited. WinVerifyTrust checks the signature and the chain but does not
    /// hand back the signer. So the file is verified first, and only then is the
    /// certificate read out of it.
    /// </para>
    /// <para>
    /// Any failure is null rather than an exception. A signature check that
    /// throws on an unreadable file would turn "cannot vouch for this" into a
    /// crash, when the two callers both want the same cautious answer.
    /// </para>
    /// </remarks>
    public static string? SignerOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            if (!Verify(path)) return null;

            // SYSLIB0057 points at X509CertificateLoader, which loads certificate
            // FILES. There is no replacement for pulling the Authenticode
            // certificate back out of a signed executable, and the alternative is
            // a second block of CryptQueryObject P/Invoke to reach the same
            // bytes. Safe here because the file has already been through
            // WinVerifyTrust above: what is being read is a signature that was
            // just confirmed to cover this file's contents.
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057

            return certificate.Subject;
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException
                                    or PlatformNotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether the file carries a signature that verifies and chains to a trusted root.</summary>
    private static bool Verify(string path)
    {
        var data = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = path,
        };

        var file = Marshal.AllocHGlobal((int)data.Size);

        try
        {
            Marshal.StructureToPtr(data, file, false);

            var trust = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),

                // No prompting, ever. This runs while the user is looking at a
                // progress bar, and a modal trust dialog from a background
                // update would be indistinguishable from malware.
                UiChoice = UiNone,
                RevocationChecks = RevokeWholeChain,
                UnionChoice = ChooseFile,
                StateAction = StateActionVerify,
                FileInfo = file,
            };

            var action = ActionGenericVerifyV2;
            var result = WinVerifyTrust(InvalidWindow, ref action, ref trust);

            // Close whatever the verify call allocated, whichever way it went.
            trust.StateAction = StateActionClose;
            WinVerifyTrust(InvalidWindow, ref action, ref trust);

            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(file);
        }
    }

    private static readonly nint InvalidWindow = -1;

    private static Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeWholeChain = 1;
    private const uint ChooseFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public nint Handle;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SubjectInterfacePackage;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Url;

        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref WinTrustData data);
}
