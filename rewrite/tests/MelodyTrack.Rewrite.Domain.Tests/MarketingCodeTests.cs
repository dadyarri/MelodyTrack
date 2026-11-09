using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class MarketingCodeTests
{
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);

    private static MarketingCampaign Campaign(int id, string code) => Ok(MarketingCampaignOperations.Create(
        new(Guid(id)), code, Period(Utc(1, 0), Utc(10, 0)), new Discount.Fixed(Ok(Money.Create(100))),
        new DiscountTerms.Payment(), null, Super, []));

    [Theory]
    [InlineData("START", " START ")]
    [InlineData("START", "start")]
    [InlineData("Старт", "старт")]
    public void Create_ExistingEquivalentCode_RejectsDuplicate(string original, string candidate)
    {
        var existing = Campaign(100, original);

        var result = MarketingCampaignOperations.Create(new(Guid(101)), candidate, existing.ActivationPeriod,
            existing.Discount, existing.Terms, null, Super, [existing]);

        AssertError(new ModelError.Duplicate(), result);
    }

    [Fact]
    public void Generate_ExistingCampaigns_ReturnsUnusedFiveCharacterCode()
    {
        var existing = Campaign(100, "START");

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var code = Ok(MarketingCodeRules.Generate([existing.Code], alphabet));

        Assert.Equal(5, code.Length);
        Assert.NotEqual(existing.Code, code);
        Assert.All(code, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"));
    }

    [Fact]
    public void Generate_AlphabetSpaceExhausted_ReturnsFailure()
    {
        var result = MarketingCodeRules.Generate(["AAAAA"], "A");

        AssertError(new ModelError.InvalidState(), result);
    }

    [Fact]
    public void Generate_EquivalentLowercaseCodeOccupiesSpace_ReturnsFailure()
    {
        AssertError(new ModelError.InvalidState(), MarketingCodeRules.Generate(["aaaaa"], "A"));
    }

    [Fact]
    public void Generate_OneFreeCode_FindsItWithoutRandomRetryFailures()
    {
        const string free = "ABABA";
        var occupied = Enumerable.Range(0, 32).Select(value => new string(Enumerable.Range(0, 5)
            .Select(position => (value & (1 << position)) == 0 ? 'A' : 'B').ToArray())).Where(code => code != free);

        var result = Ok(MarketingCodeRules.Generate(occupied, "AB"));

        Assert.Equal(free, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AA")]
    [InlineData("aA")]
    [InlineData("A-")]
    public void Generate_InvalidAlphabet_RejectsConfiguration(string alphabet)
    {
        var result = MarketingCodeRules.Generate([], alphabet);

        AssertError(new ModelError.InvalidValue(), result);
    }
}
