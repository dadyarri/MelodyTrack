using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class PaymentDiscountTests
{
    [Theory]
    [InlineData(1872.5, 1870)]
    [InlineData(1963.5, 1960)]
    public void ApplyToPayment_FractionalTotal_RoundsEntirePaymentDownToTens(double amount, int expected)
    {
        var discount = Ok(Discount.Percentage.Create(50));

        var result = Ok(discount.ApplyToPayment(Ok(Money.Create((decimal)amount * 2))));

        Assert.Equal((decimal)expected, result.Amount);
    }

    [Fact]
    public void ApplyToPayment_FixedDiscountExceedsTotal_ClampsAtZero()
    {
        var discount = new Discount.Fixed(Ok(Money.Create(2000)));

        Assert.Equal(0m, Ok(discount.ApplyToPayment(Ok(Money.Create(1000)))).Amount);
    }

    [Fact]
    public void ApplyToPayment_SubKopeckPercentage_RoundsWithoutDecisionRequired()
    {
        var discount = Ok(Discount.Percentage.Create(33.333m));

        Assert.Equal(660m, Ok(discount.ApplyToPayment(Ok(Money.Create(1000)))).Amount);
        Assert.Equal(123.45m, Ok(Money.Create(123.45m)).Amount);
    }
}
