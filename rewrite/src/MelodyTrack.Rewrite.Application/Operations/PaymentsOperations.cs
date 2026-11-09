using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public sealed record PaymentChange(Payment Before, Payment After, StaffId AuthorId, DateTimeOffset OccurredAt, string? Comment);

public static class PaymentOperations
{
    public static Result<Payment, ErrorList> Create(PaymentId id, ClientId clientId, Service service, int quantity,
        DateTimeOffset paidAt, DateTimeOffset now, string? comment, DiscountActivation? discount,
        IEnumerable<Payment> previous, IEnumerable<AppointmentSettlement> settlements, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!actor.HasPermission(StaffPermission.PaymentRecord, recordAt: paidAt, now: now))
            return ErrorList.Single(new ModelError.Forbidden());
        if (actor is Actor.Staff { Role: StaffRole.Administrator } && SchoolTime.Date(paidAt) != SchoolTime.Date(now))
            return ErrorList.Single(new ModelError.Forbidden());
        var payments = previous.ToArray();
        return PaymentRules.Create(id, clientId, service, quantity, paidAt, comment, discount, payments, settlements);
    }

    public static Result<PaymentChange, ErrorList> Edit(this Payment model, Service service, int quantity,
        DateTimeOffset paidAt, DateTimeOffset now, string? comment, DiscountActivation? discount,
        IEnumerable<Payment> previous, IEnumerable<AppointmentSettlement> history, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!actor.HasPermission(StaffPermission.PaymentCorrect, recordAt: model.PaidAt, now: now)
            || !actor.HasPermission(StaffPermission.PaymentCorrect, recordAt: paidAt, now: now))
            return ErrorList.Single(new ModelError.Forbidden());
        if (actor is Actor.Staff { Role: StaffRole.Administrator }
            && (SchoolTime.Date(model.PaidAt) != SchoolTime.Date(now) || SchoolTime.Date(paidAt) != SchoolTime.Date(now)))
            return ErrorList.Single(new ModelError.Forbidden());
        if (actor is Actor.Staff { Role: StaffRole.Administrator } && string.IsNullOrWhiteSpace(comment))
            return ErrorList.Single(new CatalogueError.Required("comment"), "comment");
        if (model.Conditions is not { } conditions) return ErrorList.Single(new ModelError.DecisionRequired(), "historicalPayment");
        if (service.Id != conditions.ServiceId) return ErrorList.Single(new ModelError.InvalidValue(), "serviceId");
        var settlements = history.ToArray();
        if (model.WasUsed(settlements)) return ErrorList.Single(new ModelError.InvalidState(), "usedPayment");
        if (discount?.Id != conditions.DiscountId || discount?.Discount != conditions.Discount)
            return ErrorList.Single(new ModelError.DecisionRequired(), "paymentDiscountCorrection");
        var payments = previous.ToArray();
        if (payments.Select(item => item.Id).Distinct().Count() != payments.Length)
            return ErrorList.Single(new ModelError.Duplicate(), "payments");
        if (payments.Any(item => item.Id == model.Id && item != model))
            return ErrorList.Single(new ModelError.InvalidState(), "payment");
        var changed = PaymentRules.Create(model.Id, model.ClientId, service, quantity, paidAt, comment, discount,
            payments.Where(item => item.Id != model.Id), settlements);
        if (changed.IsFailure) return changed.Error;
        return new PaymentChange(model, changed.Value, ((Actor.Staff)actor).Id, now.ToUniversalTime(), comment?.Trim());
    }
}
