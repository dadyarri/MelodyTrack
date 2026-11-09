using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class DiscountRules
{
    // The input is the total payment, never the price of an individual lesson (D-486).
    public static Result<Money, ErrorList> ApplyToPayment(this Discount discount, Money paymentTotal)
    {
        var amount = discount switch
        {
            Discount.Percentage percent => paymentTotal.Amount * (1 - percent.Value / 100),
            Discount.Fixed fixedDiscount => Math.Max(0, paymentTotal.Amount - fixedDiscount.Amount.Amount)
        };
        return Money.Create(decimal.Floor(amount / 10) * 10);
    }

    public static Result<Money, ErrorList> Apply(this Discount discount, Money price)
    {
        var amount = discount switch
        {
            Discount.Percentage percent => price.Amount * (1 - percent.Value / 100),
            Discount.Fixed fixedDiscount => Math.Max(0, price.Amount - fixedDiscount.Amount.Amount)
        };
        // Historical per-fact arithmetic only; current payments use ApplyToPayment.
        if (decimal.Round(amount, 2) != amount) return ErrorList.Single(new ModelError.DecisionRequired(), "rounding");
        return Money.Create(amount);
    }
}
