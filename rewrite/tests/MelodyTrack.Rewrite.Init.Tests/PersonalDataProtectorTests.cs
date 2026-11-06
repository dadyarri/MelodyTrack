using System.Security.Cryptography;
using MelodyTrack.Rewrite.Persistence.Security;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class PersonalDataProtectorTests
{
    private static string TestKey(byte value) => Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray());
    private static PersonalDataProtector Create() => new("v1", new Dictionary<string, string> { ["v1"] = TestKey(1) });
    private const string Context = "clients/test-client/notes";

    [Fact]
    public void Unprotect_IndependentFixture_ReadsDocumentedFormat()
    {
        using var protector = Create();
        // Python cryptography AESGCM: test key 01*32, nonce 00..0b, documented associated data.
        const string stored = "mtpii:1:v1:AAECAwQFBgcICQoL:m4ZFsfBO1sUu3zZG8NMYG4429KBgsWU=:sVt5xUOTwOSFTRT1m/BchQ==";

        Assert.Equal("  Заметки 🪕  ", protector.Unprotect(stored, Context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  Заметки клиента 🪕  ")]
    [InlineData("enc:это исходный текст, а не готовый шифротекст")]
    [InlineData("строка\nс переносом\0и нулём")]
    public void ProtectAndUnprotect_Text_PreservesExactValue(string text)
    {
        using var protector = Create();

        var stored = protector.Protect(text, Context);
        var restored = protector.Unprotect(stored, Context);

        Assert.NotEqual(text, stored);
        Assert.Equal(text, restored);
    }

    [Fact]
    public void ProtectAndUnprotect_Null_PreservesDatabaseNull()
    {
        using var protector = Create();

        Assert.Null(protector.Protect(null, Context));
        Assert.Null(protector.Unprotect(null, Context));
    }

    [Fact]
    public void Protect_RepeatedValue_UsesIndependentCiphertexts()
    {
        using var protector = Create();

        var first = protector.Protect("same value", Context);
        var second = protector.Protect("same value", Context);

        Assert.NotEqual(first, second);
        Assert.Equal("same value", protector.Unprotect(first, Context));
        Assert.Equal("same value", protector.Unprotect(second, Context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unprotected value")]
    [InlineData("enc:v1:old-format")]
    [InlineData("mtpii:2:v1:unsupported")]
    [InlineData("mtpii:1:v1:invalid-base64:payload:tag")]
    [InlineData("mtpii:1:v1:AA==:AA==:AA==")]
    public void Unprotect_UnrepresentableStorage_RejectsWithoutEchoingValue(string stored)
    {
        using var protector = Create();

        var error = Assert.Throws<CryptographicException>(() => protector.Unprotect(stored, Context));

        Assert.Equal("Не удалось прочитать защищённые данные.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Unprotect_TamperedAuthenticatedComponent_RejectsValue(int component)
    {
        using var protector = Create();
        var parts = protector.Protect("private text", Context).Split(':');
        var bytes = Convert.FromBase64String(parts[component]);
        bytes[0] ^= 1;
        parts[component] = Convert.ToBase64String(bytes);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(string.Join(':', parts), Context));
    }

    [Fact]
    public void Unprotect_DifferentContext_RejectsCopiedValue()
    {
        using var protector = Create();
        var stored = protector.Protect("private text", Context);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(stored, "clients/other-client/notes"));
    }

    [Fact]
    public void Unprotect_ChangedVersionLabelWithSameKey_RejectsHeaderSubstitution()
    {
        using var protector = new PersonalDataProtector("v1", new Dictionary<string, string>
        {
            ["v1"] = TestKey(1), ["v2"] = TestKey(1)
        });
        var parts = protector.Protect("private text", Context).Split(':');
        parts[2] = "v2";

        Assert.Throws<CryptographicException>(() => protector.Unprotect(string.Join(':', parts), Context));
    }

    [Fact]
    public void Unprotect_RotatedKeyRing_ReadsRetainedPreviousKey()
    {
        using var previous = Create();
        var stored = previous.Protect("previous text", Context);
        using var current = new PersonalDataProtector("v2", new Dictionary<string, string>
        {
            ["v1"] = TestKey(1), ["v2"] = TestKey(2)
        });

        Assert.Equal("previous text", current.Unprotect(stored, Context));
    }

    [Fact]
    public void Protect_RotatedKeyRing_WritesUsingCurrentKey()
    {
        using var current = new PersonalDataProtector("v2", new Dictionary<string, string>
        {
            ["v1"] = TestKey(1), ["v2"] = TestKey(2)
        });
        using var currentKeyOnly = new PersonalDataProtector("v2", new Dictionary<string, string> { ["v2"] = TestKey(2) });
        using var previousKeyOnly = Create();

        var stored = current.Protect("new text", Context);

        Assert.Equal("new text", currentKeyOnly.Unprotect(stored, Context));
        Assert.Throws<CryptographicException>(() => previousKeyOnly.Unprotect(stored, Context));
    }

    [Fact]
    public void Unprotect_DifferentKeyForSameVersion_RejectsValue()
    {
        using var writer = Create();
        using var reader = new PersonalDataProtector("v1", new Dictionary<string, string> { ["v1"] = TestKey(2) });
        var stored = writer.Protect("private text", Context);

        Assert.Throws<CryptographicException>(() => reader.Unprotect(stored, Context));
    }

    [Theory]
    [InlineData("not-a-key-and-must-not-appear-in-errors")]
    [InlineData("AA==")]
    public void Construct_InvalidKey_RejectsWithoutExposingConfiguration(string key)
    {
        var error = Assert.Throws<ArgumentException>(() => new PersonalDataProtector("v1", new Dictionary<string, string> { ["v1"] = key }));

        Assert.DoesNotContain(key, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Construct_MissingCurrentVersion_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new PersonalDataProtector("v2", new Dictionary<string, string> { ["v1"] = TestKey(1) }));
    }

    [Fact]
    public void Construct_InvalidRetainedKey_RejectsEntireConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new PersonalDataProtector("v2", new Dictionary<string, string>
        {
            ["v2"] = TestKey(2), ["v1"] = "invalid retained key"
        }));
    }

    [Fact]
    public void Construct_CaseInsensitiveInput_DoesNotAliasVersionLabels()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["v1"] = TestKey(1) };

        Assert.Throws<ArgumentException>(() => new PersonalDataProtector("V1", keys));
    }

    [Fact]
    public void Protect_InvalidUnicode_RejectsWithoutReplacingCharacters()
    {
        using var protector = Create();

        var error = Assert.Throws<ArgumentException>(() => protector.Protect("private\ud800text", Context));

        Assert.DoesNotContain("private", error.ToString());
    }

    [Fact]
    public void Dispose_AfterUse_RejectsFurtherOperations()
    {
        var protector = Create();
        var stored = protector.Protect("private text", Context);

        protector.Dispose();
        protector.Dispose();

        Assert.Throws<ObjectDisposedException>(() => protector.Protect("new text", Context));
        Assert.Throws<ObjectDisposedException>(() => protector.Unprotect(stored, Context));
    }
}
