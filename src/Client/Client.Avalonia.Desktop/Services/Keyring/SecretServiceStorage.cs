using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Tmds.DBus.Protocol;

namespace Client.Services.Keyring;

/// <summary>
/// The keyring of a Linux desktop through the freedesktop Secret Service API (GNOME Keyring,
/// KWallet, KeePassXC): the passwords go into its default collection, which the desktop unlocks
/// with the session or asks the user to.
/// </summary>
/// <remarks>
/// The messages are written by hand on top of Tmds.DBus.Protocol, as in the screen cast portal client.
/// The session is "plain": the secret crosses the session bus unencrypted, which only processes of
/// the same user can listen to.
/// </remarks>
internal sealed class SecretServiceStorage : ISecureStorage
{
    private const string Service = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string CollectionInterface = "org.freedesktop.Secret.Collection";
    private const string ItemInterface = "org.freedesktop.Secret.Item";
    private const string PromptInterface = "org.freedesktop.Secret.Prompt";
    private const string NoPrompt = "/";

    // The entries of this application; xdg:schema lets libsecret tools show them as their own
    private const string SchemaAttribute = "xdg:schema";
    private const string Schema = "com.flowrate.client.Password";
    private const string AccountAttribute = "account";

    // An unlock dialog waits for the user
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(5);

    private readonly Task<bool> _availability;

    public SecretServiceStorage()
    {
        _availability = Task.Run(IsServiceKnownAsync);
    }

    // Until the check at start has answered, a session bus is taken for a keyring
    public bool IsAvailable => _availability.IsCompletedSuccessfully ? _availability.Result : DBusAddress.Session != null;

    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        RunAsync(async connection =>
        {
            var session = await OpenSessionAsync(connection);
            var collection = await GetDefaultCollectionAsync(connection, cancellationToken);
            await UnlockAsync(connection, [collection], cancellationToken);

            var secret = Encoding.UTF8.GetBytes(password);

            try
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(Service, collection, CollectionInterface, "CreateItem", "a{sv}(oayays)b");

                var properties = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString(ItemInterface + ".Label");
                writer.WriteVariantString($"FlowRate: {username}");
                writer.WriteDictionaryEntryStart();
                writer.WriteString(ItemInterface + ".Attributes");
                writer.WriteSignature("a{ss}");
                WriteAttributes(ref writer, username);
                writer.WriteDictionaryEnd(properties);

                writer.WriteStructureStart();
                writer.WriteObjectPath(session);
                writer.WriteArray(Array.Empty<byte>());
                writer.WriteArray(secret);
                writer.WriteString("text/plain; charset=utf8");

                // An entry with the same attributes is replaced
                writer.WriteBool(true);

                var prompt = await connection.CallMethodAsync(writer.CreateMessage(), static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    reader.ReadObjectPathAsString();
                    return reader.ReadObjectPathAsString();
                });

                await PromptAsync(connection, prompt, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            return true;
        }, cancellationToken);

    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        RunAsync(async connection =>
        {
            var (unlocked, locked) = await SearchAsync(connection, username);

            if (unlocked.Length == 0 && locked.Length == 0)
                return null;

            if (unlocked.Length == 0)
            {
                await UnlockAsync(connection, locked, cancellationToken);
                unlocked = locked;
            }

            var session = await OpenSessionAsync(connection);

            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, unlocked[0], ItemInterface, "GetSecret", "o");
            writer.WriteObjectPath(session);

            var secret = await connection.CallMethodAsync(writer.CreateMessage(), static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                reader.AlignStruct();
                reader.ReadObjectPathAsString();
                reader.ReadArrayOfByte();
                return reader.ReadArrayOfByte();
            });

            try
            {
                return (string?)Encoding.UTF8.GetString(secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }, cancellationToken);

    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        RunAsync(async connection =>
        {
            var (unlocked, locked) = await SearchAsync(connection, username);

            foreach (var item in (string[])[..unlocked, ..locked])
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(Service, item, ItemInterface, "Delete");

                var prompt = await connection.CallMethodAsync(writer.CreateMessage(),
                    static (Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString());

                await PromptAsync(connection, prompt, cancellationToken);
            }

            return true;
        }, cancellationToken);

    public void Dispose()
    {
    }

    /// <summary>
    /// Every call has a connection of its own: the keyring is touched a few times a run at most.
    /// </summary>
    private static async Task<T> RunAsync<T>(Func<DBusConnection, Task<T>> call, CancellationToken cancellationToken)
    {
        var address = DBusAddress.Session
                      ?? throw new SecureStorageException(SecureStorageError.StorageUnavailable, "No D-Bus session bus");

        using var connection = new DBusConnection(address);

        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            return await call(connection).WaitAsync(cancellationToken);
        }
        catch (DBusErrorReplyException exception) when (IsMissingService(exception))
        {
            throw new SecureStorageException(SecureStorageError.StorageUnavailable, exception.Message, exception);
        }
        catch (Exception exception) when (exception is not (SecureStorageException or OperationCanceledException))
        {
            throw new SecureStorageException(SecureStorageError.StorageFailed, exception.Message, exception);
        }
    }

    private static async Task<bool> IsServiceKnownAsync()
    {
        if (DBusAddress.Session is not { } address)
            return false;

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync();

            // A keyring is often started by the bus on the first call
            return Array.IndexOf(await connection.ListServicesAsync(), Service) >= 0 ||
                   Array.IndexOf(await connection.ListActivatableServicesAsync(), Service) >= 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<string> OpenSessionAsync(DBusConnection connection)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "OpenSession", "sv");
        writer.WriteString("plain");
        writer.WriteVariantString("");

        return await connection.CallMethodAsync(writer.CreateMessage(), static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadVariantValue();
            return reader.ReadObjectPathAsString();
        });
    }

    /// <summary>
    /// The collection the desktop keeps passwords in. A keyring that has none yet creates one, which
    /// asks the user for its password.
    /// </summary>
    private static async Task<string> GetDefaultCollectionAsync(DBusConnection connection,
        CancellationToken cancellationToken)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "ReadAlias", "s");
        writer.WriteString("default");

        var collection = await connection.CallMethodAsync(writer.CreateMessage(),
            static (Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString());

        if (collection != NoPrompt)
            return collection;

        writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "CreateCollection", "a{sv}s");

        var properties = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString(CollectionInterface + ".Label");
        writer.WriteVariantString("Login");
        writer.WriteDictionaryEnd(properties);
        writer.WriteString("default");

        var (created, prompt) = await connection.CallMethodAsync(writer.CreateMessage(),
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
            });

        if (created != NoPrompt)
            return created;

        // Made once the user has answered: the path comes with the completion of the prompt
        var result = await PromptAsync(connection, prompt, cancellationToken);
        return result?.GetObjectPathAsString()
               ?? throw new SecureStorageException(SecureStorageError.StorageFailed, "The keyring created no collection");
    }

    private static async Task UnlockAsync(DBusConnection connection, string[] objects, CancellationToken cancellationToken)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "Unlock", "ao");
        writer.WriteArray(Array.ConvertAll(objects, path => new ObjectPath(path)));

        var prompt = await connection.CallMethodAsync(writer.CreateMessage(), static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadArrayOfObjectPath();
            return reader.ReadObjectPathAsString();
        });

        await PromptAsync(connection, prompt, cancellationToken);
    }

    private static async Task<(string[] Unlocked, string[] Locked)> SearchAsync(DBusConnection connection, string username)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, ServicePath, ServiceInterface, "SearchItems", "a{ss}");
        WriteAttributes(ref writer, username);

        return await connection.CallMethodAsync(writer.CreateMessage(), static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            var unlocked = Array.ConvertAll(reader.ReadArrayOfObjectPath(), path => path.ToString());
            var locked = Array.ConvertAll(reader.ReadArrayOfObjectPath(), path => path.ToString());
            return (unlocked, locked);
        });
    }

    /// <summary>
    /// Shows the dialog the keyring asked for, e.g. to unlock a collection, and waits for the user.
    /// </summary>
    /// <returns>What the completed prompt carries, null when no prompt was needed.</returns>
    private static async Task<VariantValue?> PromptAsync(DBusConnection connection, string prompt,
        CancellationToken cancellationToken)
    {
        if (prompt == NoPrompt)
            return null;

        var completion = new TaskCompletionSource<(bool Dismissed, VariantValue Result)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Path = prompt,
            Interface = PromptInterface,
            Member = "Completed"
        };

        // Watched before the call, so the answer cannot be missed
        using var subscription = await connection.AddMatchAsync(rule,
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadBool(), reader.ReadVariantValue());
            },
            static (Notification<(bool, VariantValue)> notification) =>
            {
                var source = (TaskCompletionSource<(bool, VariantValue)>)notification.State!;

                if (notification.HasValue)
                    source.TrySetResult(notification.Value);
                else if (notification.Exception != null)
                    source.TrySetException(notification.Exception);
            },
            false, ObserverFlags.EmitOnConnectionClosed, completion);

        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, prompt, PromptInterface, "Prompt", "s");

        // No parent window: the dialog is simply not attached to ours
        writer.WriteString("");
        await connection.CallMethodAsync(writer.CreateMessage());

        var (dismissed, result) = await completion.Task.WaitAsync(PromptTimeout, cancellationToken);

        if (dismissed)
            throw new SecureStorageException(SecureStorageError.StorageFailed, "The keyring dialog was dismissed");

        return result;
    }

    private static void WriteAttributes(ref MessageWriter writer, string username)
    {
        var attributes = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString(SchemaAttribute);
        writer.WriteString(Schema);
        writer.WriteDictionaryEntryStart();
        writer.WriteString(AccountAttribute);
        writer.WriteString(username);
        writer.WriteDictionaryEnd(attributes);
    }

    private static bool IsMissingService(DBusErrorReplyException exception) =>
        exception.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown"
            or "org.freedesktop.DBus.Error.NameHasNoOwner";
}
