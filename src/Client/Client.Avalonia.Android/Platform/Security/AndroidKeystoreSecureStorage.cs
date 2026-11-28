using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Client.Application.Ports;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace Client.Droid.Platform.Security;

/// <summary>
/// The keyring of the app: secrets encrypted with an AES key of the Android Keystore, which never leaves it, and kept
/// in the private preferences of the app.
/// </summary>
internal sealed class AndroidKeystoreSecureStorage : ISecureStorage
{
    private const string Provider = "AndroidKeyStore";
    private const string KeyAlias = "flowrate-secrets";
    private const string PreferencesName = "flowrate-secrets";
    private const int NonceLength = 12;
    private const int TagBits = 128;

    private readonly ISharedPreferences _preferences =
        Android.App.Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public bool IsAvailable => true;

    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(Javax.Crypto.CipherMode.EncryptMode, Key());
                var sealedBytes = cipher.DoFinal(Encoding.UTF8.GetBytes(password))!;
                var stored = new byte[NonceLength + sealedBytes.Length];
                cipher.GetIV()!.CopyTo(stored, 0);
                sealedBytes.CopyTo(stored, NonceLength);

                using var editor = _preferences.Edit()!;
                editor.PutString(username, Convert.ToBase64String(stored));
                if (!editor.Commit())
                    throw new SecureStorageException(SecureStorageError.StorageFailed, "The preferences were not written.");
            }
            catch (Java.Lang.Exception exception)
            {
                throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message ?? "Keystore", exception);
            }
        }, cancellationToken);

    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (_preferences.GetString(username, null) is not { } text)
                return null;

            try
            {
                var stored = Convert.FromBase64String(text);
                var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(Javax.Crypto.CipherMode.DecryptMode, Key(), new GCMParameterSpec(TagBits, stored[..NonceLength]));
                var plain = cipher.DoFinal(stored[NonceLength..])!;
                try
                {
                    return (string?)Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            catch (Java.Lang.Exception exception)
            {
                // A key reset with the screen lock, or a backup restored to another phone: the secret is lost
                throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message ?? "Keystore", exception);
            }
        }, cancellationToken);

    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var editor = _preferences.Edit()!;
            editor.Remove(username);
            editor.Commit();
        }, cancellationToken);

    private static IKey Key()
    {
        var store = KeyStore.GetInstance(Provider)!;
        store.Load(null);
        if (store.GetKey(KeyAlias, null) is { } existing)
            return existing;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return generator.GenerateKey()!;
    }

    public void Dispose()
    {
    }
}
