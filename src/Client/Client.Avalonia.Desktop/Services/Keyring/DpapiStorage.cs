using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Tools.AppInfo;

namespace Client.Services.Keyring;

/// <summary>
/// The keyring of Windows: each password is a file in the data folder encrypted with DPAPI for the
/// user account, so only this user on this machine opens it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class DpapiStorage : ISecureStorage
{
    private const string FolderName = "keyring";

    // CRYPTPROTECT_UI_FORBIDDEN: never a dialog, the call fails instead
    private const int UiForbidden = 0x1;

    // Ties the blobs to this application on top of the user account
    private static readonly byte[] Entropy = "com.flowrate.client"u8.ToArray();

    private readonly string _directory = Path.Combine(AppDirectories.Data, FolderName);

    public bool IsAvailable => true;

    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var plain = Encoding.UTF8.GetBytes(password);

            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(PathOf(username), Protect(plain));
            }
            catch (IOException exception)
            {
                throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message, exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }, cancellationToken);

    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            byte[] blob;

            try
            {
                blob = File.ReadAllBytes(PathOf(username));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException exception)
            {
                throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message, exception);
            }

            var plain = Unprotect(blob);

            try
            {
                return (string?)Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }, cancellationToken);

    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                File.Delete(PathOf(username));
            }
            catch (IOException exception)
            {
                throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message, exception);
            }
        }, cancellationToken);

    public void Dispose()
    {
    }

    // The account names are ours, never typed by the user
    private string PathOf(string username) => Path.Combine(_directory, username + ".dpapi");

    private static unsafe byte[] Protect(byte[] plain)
    {
        fixed (byte* data = plain)
        fixed (byte* entropy = Entropy)
        {
            var input = new DataBlob(plain.Length, data);
            var salt = new DataBlob(Entropy.Length, entropy);

            if (!CryptProtectData(in input, null, in salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output))
                throw Failed("CryptProtectData");

            return Take(output);
        }
    }

    private static unsafe byte[] Unprotect(byte[] blob)
    {
        fixed (byte* data = blob)
        fixed (byte* entropy = Entropy)
        {
            var input = new DataBlob(blob.Length, data);
            var salt = new DataBlob(Entropy.Length, entropy);

            if (!CryptUnprotectData(in input, IntPtr.Zero, in salt, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output))
                throw Failed("CryptUnprotectData");

            return Take(output);
        }
    }

    private static unsafe byte[] Take(DataBlob blob)
    {
        try
        {
            return new ReadOnlySpan<byte>(blob.Data, blob.Size).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(new Span<byte>(blob.Data, blob.Size));
            LocalFree((IntPtr)blob.Data);
        }
    }

    private static SecureStorageException Failed(string call) =>
        new(SecureStorageError.StorageFailed, $"{call} failed: {Marshal.GetLastPInvokeError()}");

    [StructLayout(LayoutKind.Sequential)]
    private readonly unsafe struct DataBlob(int size, byte* data)
    {
        public readonly int Size = size;
        public readonly byte* Data = data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(in DataBlob dataIn, string? description, in DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(in DataBlob dataIn, IntPtr description, in DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
