using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Deckside.Directory;

/// <summary>
/// Decrypts the DPAPI-NG part of <c>msLAPS-EncryptedPassword</c> with <c>NCryptUnprotectSecret</c>, as
/// <c>Get-LapsADPassword</c> does. It runs with the identity of the Windows session (not the account typed at
/// login): that user must be an authorized decryptor of the LAPS policy.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class LapsDecryptor
{
    private const uint NcryptSilentFlag = 0x40;

    public static string Decrypt(byte[] protectedBlob)
    {
        var status = NCryptUnprotectSecret(IntPtr.Zero, NcryptSilentFlag, protectedBlob, (uint)protectedBlob.Length, IntPtr.Zero, IntPtr.Zero, out var data, out var length);
        if (status != 0)
        {
            throw new Win32Exception(status);
        }

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            try
            {
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            // The plaintext lives in unmanaged memory: wipe it before freeing.
            for (var i = 0; i < length; i++)
            {
                Marshal.WriteByte(data, i, 0);
            }

            LocalFree(data);
        }
    }

    [LibraryImport("ncrypt.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NCryptUnprotectSecret(IntPtr descriptor, uint flags, byte[] protectedBlob, uint protectedBlobLength, IntPtr memPara, IntPtr window, out IntPtr data, out uint dataLength);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr LocalFree(IntPtr memory);
}
