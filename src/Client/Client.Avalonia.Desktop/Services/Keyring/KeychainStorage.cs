using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;

namespace Client.Services.Keyring;

/// <summary>
/// The keyring of macOS: each password is a generic password item of the login keychain.
/// </summary>
/// <remarks>
/// The keychain lets the application that made an item read it without asking. A build with
/// another code signature, an update of an unsigned build as well, is asked once whether it may.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed partial class KeychainStorage : ISecureStorage
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ServiceName = "com.flowrate.client";

    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;
    private const uint Utf8Encoding = 0x08000100;

    public bool IsAvailable => true;

    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var plain = Encoding.UTF8.GetBytes(password);
            using var scope = new Scope();

            try
            {
                var data = scope.Data(plain);
                var status = SecItemAdd(Query(scope, username,
                    (Constant(Security, "kSecValueData"), data),
                    (Constant(Security, "kSecAttrLabel"), scope.String($"FlowRate: {username}"))), IntPtr.Zero);

                if (status == DuplicateItem)
                    status = SecItemUpdate(Query(scope, username),
                        scope.Dictionary((Constant(Security, "kSecValueData"), data)));

                Check(status, "SecItemAdd");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }, cancellationToken);

    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var scope = new Scope();
            var status = SecItemCopyMatching(Query(scope, username,
                (Constant(Security, "kSecReturnData"), Constant(CoreFoundation, "kCFBooleanTrue")),
                (Constant(Security, "kSecMatchLimit"), Constant(Security, "kSecMatchLimitOne"))), out var data);

            if (status == ItemNotFound)
                return null;
            Check(status, "SecItemCopyMatching");

            try
            {
                var length = (int)CFDataGetLength(data);
                var plain = new byte[length];
                Marshal.Copy(CFDataGetBytePtr(data), plain, 0, length);

                try
                {
                    return (string?)Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            finally
            {
                CFRelease(data);
            }
        }, cancellationToken);

    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var scope = new Scope();
            var status = SecItemDelete(Query(scope, username));

            if (status != ItemNotFound)
                Check(status, "SecItemDelete");
        }, cancellationToken);

    public void Dispose()
    {
    }

    // The item of an account, with whatever else the call takes
    private static IntPtr Query(Scope scope, string username, params ReadOnlySpan<(IntPtr Key, IntPtr Value)> more)
    {
        Span<(IntPtr, IntPtr)> entries = new (IntPtr, IntPtr)[3 + more.Length];
        entries[0] = (Constant(Security, "kSecClass"), Constant(Security, "kSecClassGenericPassword"));
        entries[1] = (Constant(Security, "kSecAttrService"), scope.String(ServiceName));
        entries[2] = (Constant(Security, "kSecAttrAccount"), scope.String(username));
        more.CopyTo(entries[3..]);

        return scope.Dictionary(entries);
    }

    private static void Check(int status, string call)
    {
        if (status != Success)
            throw new SecureStorageException(SecureStorageError.StorageFailed, $"{call} failed: {status}");
    }

    // The value of an exported constant such as kSecClass, which is a pointer to the real object
    private static IntPtr Constant(string library, string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(library), name));

    /// <summary>The CoreFoundation objects made for one call, released together.</summary>
    private sealed class Scope : IDisposable
    {
        private readonly System.Collections.Generic.List<IntPtr> _objects = [];

        public IntPtr String(string value) => Keep(CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding));

        public unsafe IntPtr Data(byte[] bytes)
        {
            fixed (byte* pointer = bytes)
                return Keep(CFDataCreate(IntPtr.Zero, pointer, bytes.Length));
        }

        public unsafe IntPtr Dictionary(params ReadOnlySpan<(IntPtr Key, IntPtr Value)> entries)
        {
            var keys = stackalloc IntPtr[entries.Length];
            var values = stackalloc IntPtr[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                keys[index] = entries[index].Key;
                values[index] = entries[index].Value;
            }

            var library = NativeLibrary.Load(CoreFoundation);
            return Keep(CFDictionaryCreate(IntPtr.Zero, keys, values, entries.Length,
                NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks")));
        }

        private IntPtr Keep(IntPtr instance)
        {
            if (instance == IntPtr.Zero)
                throw new SecureStorageException(SecureStorageError.StorageFailed, "CoreFoundation allocation failed");

            _objects.Add(instance);
            return instance;
        }

        public void Dispose()
        {
            foreach (var instance in _objects)
                CFRelease(instance);
        }
    }

    [LibraryImport(Security)]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemUpdate(IntPtr query, IntPtr attributes);

    [LibraryImport(Security)]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemDelete(IntPtr query);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static unsafe partial IntPtr CFDataCreate(IntPtr allocator, byte* bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static unsafe partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr* keys, IntPtr* values, nint count,
        IntPtr keyCallBacks, IntPtr valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr instance);
}
