using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

/// <summary>A non-negative amount in roubles, with kopeck precision.</summary>
public sealed record Money
{
    private Money(decimal amount) => Amount = amount;
    public decimal Amount { get; }
    public static Result<Money, ErrorList> Create(decimal amount) => amount < 0 || decimal.Round(amount, 2) != amount
        ? ErrorList.Single(new ModelError.InvalidValue(), "amount") : new Money(amount);
}
