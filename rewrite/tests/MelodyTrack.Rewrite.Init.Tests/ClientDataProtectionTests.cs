using System.Security.Cryptography;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Security;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientDataProtectionTests
{
    private static readonly ClientId Owner = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly ClientId OtherOwner = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private static readonly ClientContactId Contact = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000003"));
    private static readonly ClientContactId OtherContact = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000004"));
    private static PersonalDataProtector Create() => new("v1", new Dictionary<string, string>
    {
        ["v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray())
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  заметки  ")]
    public void ProtectAndUnprotectNotes_OptionalText_PreservesValue(string? notes)
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);

        Assert.Equal(notes, fields.UnprotectNotes(Owner, fields.ProtectNotes(Owner, notes)));
    }

    [Fact]
    public void UnprotectNotes_DifferentClient_RejectsCopiedNotes()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);
        var stored = fields.ProtectNotes(Owner, "private notes");

        Assert.Throws<CryptographicException>(() => fields.UnprotectNotes(OtherOwner, stored));
    }

    [Fact]
    public void ProtectAndUnprotectValue_OriginalSpacing_PreservesContact()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);
        const string value = "  +44 20 1234 5678  ";

        Assert.Equal(value, fields.UnprotectValue(Owner, Contact, fields.ProtectValue(Owner, Contact, value)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  родитель  ")]
    public void ProtectAndUnprotectComment_OptionalText_PreservesValue(string? comment)
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);

        Assert.Equal(comment, fields.UnprotectComment(Owner, Contact, fields.ProtectComment(Owner, Contact, comment)));
    }

    [Fact]
    public void UnprotectValue_DifferentOwner_RejectsMovedContact()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);
        var stored = fields.ProtectValue(Owner, Contact, "+44 20 1234 5678");

        Assert.Throws<CryptographicException>(() => fields.UnprotectValue(OtherOwner, Contact, stored));
    }

    [Fact]
    public void UnprotectValue_DifferentContact_RejectsCopiedValue()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);
        var stored = fields.ProtectValue(Owner, Contact, "+44 20 1234 5678");

        Assert.Throws<CryptographicException>(() => fields.UnprotectValue(Owner, OtherContact, stored));
    }

    [Fact]
    public void UnprotectComment_ContactValueInCommentField_RejectsFieldSubstitution()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);
        var stored = fields.ProtectValue(Owner, Contact, "+44 20 1234 5678");

        Assert.Throws<CryptographicException>(() => fields.UnprotectComment(Owner, Contact, stored));
    }

    [Fact]
    public void ProtectNotes_EmptyOwner_RejectsUnboundData()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);

        Assert.Throws<ArgumentException>(() => fields.ProtectNotes(default, "notes"));
    }

    [Fact]
    public void ProtectValue_EmptyContact_RejectsUnboundData()
    {
        using var protector = Create();
        var fields = new ClientDataProtection(protector);

        Assert.Throws<ArgumentException>(() => fields.ProtectValue(Owner, default, "value"));
    }
}
