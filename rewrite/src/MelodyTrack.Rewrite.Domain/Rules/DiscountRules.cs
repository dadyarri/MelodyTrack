using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class DiscountRules
{
    public static Result<Money, ErrorList> Apply(this Discount discount, Money price)
    {
        var amount = discount switch
        {
            Discount.Percentage percent => price.Amount * (1 - percent.Value / 100),
            Discount.Fixed fixedDiscount => Math.Max(0, price.Amount - fixedDiscount.Amount.Amount)
        };
        // Rounding policy has not been agreed. Do not silently round financial facts.
        if (decimal.Round(amount, 2) != amount) return ErrorList.Single(new ModelError.DecisionRequired(), "rounding");
        return Money.Create(amount);
    }
}
