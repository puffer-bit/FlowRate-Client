using System.Security.Cryptography;
using Client.Application.Security;
using Xunit;

namespace Client.Application.Tests;

public class RecoveryPhraseTests
{
    // BIP-39 test vector: 32 bytes of 0x7f give this phrase
    private const string VectorPhrase =
        "legal winner thank year wave sausage worth useful legal winner thank year wave sausage worth useful " +
        "legal winner thank year wave sausage worth title";

    [Fact]
    public void Phrase_FollowsBip39()
    {
        var entropy = Enumerable.Repeat((byte)0x7f, 32).ToArray();

        Assert.Equal(VectorPhrase, RecoveryPhrase.FromEntropy(entropy));
        Assert.Equal(entropy, RecoveryPhrase.TryParse(VectorPhrase));
    }

    [Fact]
    public void Parse_TakesWhatPeopleType_AndRefusesMistakes()
    {
        var phrase = RecoveryPhrase.Generate();
        var words = phrase.Split(' ');

        // Numbered, upper case, first four letters, commas
        var typed = string.Join(", ", words.Select((word, i) => $"{i + 1}. {word[..Math.Min(4, word.Length)].ToUpperInvariant()}"));
        Assert.Equal(phrase, RecoveryPhrase.Normalize(typed));

        Assert.Null(RecoveryPhrase.TryParse(string.Join(' ', words.Take(23))));
        Assert.Null(RecoveryPhrase.TryParse(string.Join(' ', words.Take(23).Append("notaword"))));
        // Two words swapped break the checksum, nearly always
        var swapped = (string[])words.Clone();
        (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
        if (swapped[0] != swapped[1])
            Assert.NotEqual(phrase, RecoveryPhrase.Normalize(string.Join(' ', swapped)) ?? "");
    }

    [Fact]
    public void Keys_AreTheSameForTheSamePhrase_AndSignVerifiably()
    {
        var phrase = RecoveryPhrase.Generate();
        using var first = RecoveryKeySet.FromPhrase(phrase)!;
        using var second = RecoveryKeySet.FromPhrase(phrase)!;

        Assert.Equal(first.RecoveryPublicKey, second.RecoveryPublicKey);
        Assert.Equal(first.AccountPublicKey, second.AccountPublicKey);
        Assert.Equal(first.BackupKey, second.BackupKey);
        Assert.NotEqual(first.RecoveryPublicKey, first.AccountPublicKey);

        // The public point computed here is the one of the private scalar
        var payload = "challenge"u8.ToArray();
        var signature = VaultDeviceKeyStore.Sign(first.RecoveryKey, payload);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(first.RecoveryPublicKey, out _);
        Assert.True(verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task InMemoryKeys_SignUntilDeleted()
    {
        var store = new InMemoryDeviceKeyStore();
        var publicKey = await store.CreateAsync("device");
        var data = "nonce"u8.ToArray();

        var signature = await store.SignAsync("device", data);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        Assert.True(verifier.VerifyData(data, signature!, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        await store.DeleteAsync("device");
        Assert.Null(await store.SignAsync("device", data));
    }
}
