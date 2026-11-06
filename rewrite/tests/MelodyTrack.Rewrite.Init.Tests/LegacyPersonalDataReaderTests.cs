using System.Security.Cryptography;
using MelodyTrack.Rewrite.Persistence.Import;
using MelodyTrack.Rewrite.Persistence.Security;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class LegacyPersonalDataReaderTests
{
    // Independent AES-GCM fixture, generated with Python cryptography from the pinned source's derivation.
    // This is public test-only key material, unrelated to any deployment.
    private const string MasterKey = "legacy-test-only-master-key-32-characters";
    private const string Stored = "enc:old:AAECAwQFBgcICQoL:x0b/a+KghRvLCsiTyTbRBzRd98w=:G1Y95tIjCIKiQMqMq5VLxw==";
    private const string Plaintext = "  +44 20 1234 5678  ";
    private static LegacyPersonalDataReader Create() => new(new Dictionary<string, string> { ["old"] = MasterKey });

    [Fact]
    public void Read_ProductionFormatFixture_RestoresExactText()
    {
        using var reader = Create();

        Assert.Equal(Plaintext, reader.Read(Stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("  historical plaintext  ")]
    public void Read_UnencryptedLegacyValue_PreservesOriginal(string? value)
    {
        using var reader = Create();

        Assert.Equal(value, reader.Read(value));
    }

    [Fact]
    public void Read_LegacyUnicodeVersion_SupportsPreviouslyValidConfiguration()
    {
        using var reader = new LegacyPersonalDataReader(new Dictionary<string, string> { ["версия"] = MasterKey });

        Assert.Equal(Plaintext, reader.Read(Stored.Replace("enc:old:", "enc:версия:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Read_UnknownKeyVersion_RejectsWithoutLeakingSource()
    {
        using var reader = new LegacyPersonalDataReader(new Dictionary<string, string>());

        var error = Assert.Throws<CryptographicException>(() => reader.Read(Stored));

        Assert.Equal("Не удалось прочитать защищённые данные источника.", error.Message);
        Assert.DoesNotContain(Stored, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Read_WrongMasterKey_RejectsInsteadOfRepairingValue()
    {
        using var reader = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = new('x', 40) });

        Assert.Throws<CryptographicException>(() => reader.Read(Stored));
    }

    [Theory]
    [InlineData("enc:")]
    [InlineData("enc:old:missing-parts")]
    [InlineData("enc:old:bad-base64:AA==:AA==")]
    [InlineData("enc:old:AA==:AA==:AA==")]
    [InlineData("enc:old:AAECAwQFBgcICQoL:x0b/a+KghRvLCsiTyTbRBzRd98w=:AAAAAAAAAAAAAAAAAAAAAA==")]
    public void Read_InvalidEncryptedValue_RejectsInsteadOfReturningCiphertext(string value)
    {
        using var reader = Create();

        Assert.Throws<CryptographicException>(() => reader.Read(value));
    }

    [Fact]
    public void ReadAndProtect_LegacyValue_ReencryptsForTargetContext()
    {
        using var reader = Create();
        using var target = new PersonalDataProtector("new", new Dictionary<string, string>
        {
            ["new"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        });

        var stored = target.Protect(reader.Read(Stored), "clients/new-owner/contact/value");

        Assert.NotEqual(Stored, stored);
        Assert.Equal(Plaintext, target.Unprotect(stored, "clients/new-owner/contact/value"));
    }

    [Fact]
    public void Dispose_CompletedImport_RejectsFurtherReads()
    {
        var reader = Create();
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.Read(Stored));
    }
}
