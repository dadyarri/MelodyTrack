using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientContactComparisonTests
{
    [Theory]
    [InlineData("8 (912) 345-67-89", "+7 912 345 67 89")]
    [InlineData("+44 (20) 1234-5678", "442012345678")]
    public void Normalize_PhoneFormattingAndRussianTrunkPrefix_Matches(string first, string second)
    {
        Assert.Equal(ClientContactComparison.Normalize(ContactFormat.Phone, first),
            ClientContactComparison.Normalize(ContactFormat.Phone, second));
    }

    [Theory]
    [InlineData("8 123", "+7 123")]
    [InlineData("8 912 345 67 890", "7 912 345 67 890")]
    public void Normalize_PhoneEightOnlyChangesForElevenDigits(string first, string second)
    {
        Assert.NotEqual(ClientContactComparison.Normalize(ContactFormat.Phone, first),
            ClientContactComparison.Normalize(ContactFormat.Phone, second));
    }

    [Fact]
    public void Normalize_ExplicitInternationalEightPrefix_IsPreserved()
    {
        Assert.NotEqual(ClientContactComparison.Normalize(ContactFormat.Phone, "+81 90 1234 5678"),
            ClientContactComparison.Normalize(ContactFormat.Phone, "+7 90 1234 5678"));
    }

    [Fact]
    public void Normalize_EmailCaseAndPlusSuffix_AreIgnored()
    {
        Assert.Equal(ClientContactComparison.Normalize(ContactFormat.Email, "Name+lesson@Example.com"),
            ClientContactComparison.Normalize(ContactFormat.Email, "name@example.COM"));
    }
}
