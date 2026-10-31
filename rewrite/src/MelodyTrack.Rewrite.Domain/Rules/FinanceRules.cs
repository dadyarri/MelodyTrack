using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class PaymentRules
{
    public static Result<Payment, ErrorList> Create(PaymentId id, ClientId clientId, Money amount, DateTimeOffset paidAt, string? comment)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"));
        if (validation.IsFailure) return validation.Error;
        if (amount.Amount == 0) return ErrorList.Single(new ModelError.InvalidValue(), "amount");
        return new Payment(id, clientId, amount, paidAt, Validation.OptionalText(comment));
    }
}

public static class RefundRules
{
    public static Result<Refund, ErrorList> Create(RefundId id, ClientId clientId, Money amount, DateTimeOffset refundedAt, string? comment)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"));
        if (validation.IsFailure) return validation.Error;
        if (amount.Amount == 0) return ErrorList.Single(new ModelError.InvalidValue(), "amount");
        return new Refund(id, clientId, amount, refundedAt, Validation.OptionalText(comment));
    }
}

public static class ExpenseCategoryRules
{
    public static Result<ExpenseCategory, ErrorList> Create(ExpenseCategoryId id, string? name)
    {
        var text = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text));
        return validation.IsFailure ? validation.Error : new ExpenseCategory(id, text.Value);
    }
}

public static class ExpenseRules
{
    public static Result<Expense, ErrorList> Create(ExpenseId id, string? description, Money amount, DateOnly date, ExpenseCategoryId? categoryId)
    {
        var text = Validation.Required("description", description);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text),
            categoryId is { } category ? Validation.Identifier(category.Value, "categoryId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return new Expense(id, text.Value, amount, date, categoryId);
    }
}

public static class AppointmentSettlementRules
{
    public static Result<AppointmentSettlement, ErrorList> Create(SettlementId id, Appointment appointment, SettlementSource source, DateTimeOffset recordedAt, IEnumerable<AppointmentSettlement> history)
    {
        var validation = Validation.Identifier(id.Value);
        if (validation.IsFailure) return validation.Error;
        if (appointment.IsDeleted || appointment.Status is not (AppointmentStatus.Completed or AppointmentStatus.Burned))
            return ErrorList.Single(new ModelError.InvalidState());
        if (history.Any(item => item.Id == id || item.AppointmentId == appointment.Id && !item.IsReversed))
            return ErrorList.Single(new FinanceError.AlreadySettled());
        var sourceValidation = source switch
        {
            SettlementSource.Subscription subscription => Validation.Identifier(subscription.Id.Value, "subscriptionId"),
            SettlementSource.Cash { DiscountId: { } discount } => Validation.Identifier(discount.Value, "discountId"),
            SettlementSource.Cash => UnitResult.Success<ErrorList>()
        };
        if (sourceValidation.IsFailure) return sourceValidation.Error;
        return new AppointmentSettlement(id, appointment, source, recordedAt);
    }

    public static AppointmentSettlement Reverse(this AppointmentSettlement model) => model with { IsReversed = true };
}
