namespace Client.Core.Push;

/// <summary>
/// The key a coordinator encrypts its push notifications with. The coordinator writes
/// <paramref name="KeyId"/> into every push in the clear, so the device finds the key without the
/// push naming the coordinator.
/// </summary>
/// <param name="KeyId">Random bytes in hex.</param>
/// <param name="Key">An AES-256 key.</param>
public sealed record PushKey(string KeyId, string CoordinatorInstanceId, byte[] Key);
