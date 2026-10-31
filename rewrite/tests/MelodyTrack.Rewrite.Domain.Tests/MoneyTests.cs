using MelodyTrack.Rewrite.Application;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class MoneyTests
{
    public static TheoryData<decimal> InvalidAmounts => new() { -1m, 1.005m };

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void Create_InvalidRoubleAmount_RejectsValue(decimal amount)
    {
        AssertError(new ModelError.InvalidValue(), Money.Create(amount));
    }

    [Fact]
    public void Create_Kopecks_PreservesExactAmount()
    {
        Assert.Equal(10.01m, Ok(Money.Create(10.01m)).Amount);
    }
}
