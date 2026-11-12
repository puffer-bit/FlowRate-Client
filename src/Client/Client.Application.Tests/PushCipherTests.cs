using Client.Application.Push;
using Client.Core.Push;
using Xunit;

namespace Client.Application.Tests;

public class PushCipherTests
{
    // The same vector is opened by the server's cipher in its own tests, so the two stay compatible
    private const string Sealed =
        "ZGVmZ2hpamtsbW5vMzm9CRabMvdQAyuHqCwO33jgZWXkHpcbybDYJ4mBiWr3gSG0BXa5PsrfWlyVI76Bejb9xRpoJk2UlN63g6OJqGXpqH23LF5M703XdyOmE5UBFntR1Yzp5OXAGuYIdiA16wPcHwYomkSRm7KswWiaxABf5We1W4MWYsZCMQoM2HXiKkkqScLds0lreifo9Cjj9a+QIz4/pKgBvVw+hX/RrvlcQn6q7dbZWmCZxdxEk/6mwbAV9bTh0U81+8ADOWyusev0";

    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Decrypt_OpensWhatTheServerSealed()
    {
        var payload = PushCipher.Decrypt(Key, Sealed);

        Assert.Equal(new PushPayload("coordinator", 7, "general", 42, 1, "alice", "привет 😀",
            PushAttachmentKind.None, 1791882000000), payload);
    }

    [Fact]
    public void CallPush_DecryptsCallIdentityStateAndExpiry()
    {
        var id = Guid.NewGuid();
        var json = System.Text.Encoding.UTF8.GetBytes($$"""
            {"coordinatorId":"coordinator","chatId":7,"chatName":"alice","senderId":1,
             "senderName":"alice","sentTime":1000,"callId":"{{id}}","callState":0,
             "expiresAt":46000,"recipientId":2}
            """);
        var bytes = new byte[12 + json.Length + 16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes.AsSpan(0, 12));
        using (var aes = new System.Security.Cryptography.AesGcm(Key, 16))
            aes.Encrypt(bytes.AsSpan(0, 12), json, bytes.AsSpan(12, json.Length), bytes.AsSpan(12 + json.Length));
        var call = Assert.IsType<PushPayload>(PushCipher.Decrypt(Key, Convert.ToBase64String(bytes)));
        Assert.Equal(id, call.CallId);
        Assert.Equal(0, call.CallState);
        Assert.Equal(2, call.RecipientId);
        Assert.Equal(46000, call.ExpiresAt);
    }

    [Fact]
    public void Decrypt_WithAnotherKey_ReturnsNull()
    {
        var key = Key.ToArray();
        key[0] ^= 1;

        Assert.Null(PushCipher.Decrypt(key, Sealed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAA")]
    [InlineData("not base64!")]
    public void Decrypt_OfMalformedContent_ReturnsNull(string content)
    {
        Assert.Null(PushCipher.Decrypt(Key, content));
    }

    [Fact]
    public void Decrypt_OfChangedContent_ReturnsNull()
    {
        var bytes = Convert.FromBase64String(Sealed);
        bytes[20] ^= 1;

        Assert.Null(PushCipher.Decrypt(Key, Convert.ToBase64String(bytes)));
    }
}
