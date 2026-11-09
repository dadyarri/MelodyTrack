using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SchedulingUsageRules
{
    public static bool CanDelete(this SchedulingUsage model) => !model.HasFutureAppointments && !model.HasActiveSeries && !model.HasHistoricalUse;
}

public static class StaffRules
{
    public static Result<Staff, ErrorList> Create(StaffId id, string? name, StaffRole role, bool participates)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new Staff(id, validName.Value, role, participates);
    }

    public static Staff SetParticipation(this Staff model, bool participates) => model with { Participates = participates };

    public static Staff Block(this Staff model) => model with { IsBlocked = true };

    public static Staff Unblock(this Staff model) => model with { IsBlocked = false };

    public static Staff ChangeRole(this Staff model, StaffRole role) => model with { Role = role };

    public static Staff Unarchive(this Staff model) => model with { IsArchived = false };

    public static Result<Staff, ErrorList> Archive(this Staff model, SchedulingUsage usage, bool isSoleTeacherOnUnfinishedCourse)
    {
        var validation = Validation.Collect(Validation.Archive(usage), isSoleTeacherOnUnfinishedCourse
            ? UnitResult.Failure(ErrorList.Single(new CatalogueError.SoleCourseTeacher())) : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return model with { IsArchived = true };
    }

    public static bool ParticipatesInScheduling(this Staff model) => model.Participates && !model.IsArchived;
}

public static class RoomRules
{
    public static Result<Room, ErrorList> Create(RoomId id, string? name, string? comment)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new Room(id, validName.Value, Validation.OptionalText(comment));
    }

    public static Room Archive(this Room model) => model with { IsArchived = true };

    public static Room Unarchive(this Room model) => model with { IsArchived = false };
}

public static class ServiceRules
{
    public static Result<Service, ErrorList> Create(ServiceId id, string? name, LessonDuration duration, bool isConsultation, ServicePrice initialPrice)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        if (isConsultation && initialPrice.Amount != 0)
            return ErrorList.Single(new ModelError.InvalidValue(), "initialPrice");
        return new Service(id, validName.Value, duration, isConsultation, initialPrice);
    }

    public static Result<Service, ErrorList> Restore(ServiceId id, string? name, LessonDuration duration,
        bool isConsultation, IEnumerable<ServicePrice> prices)
    {
        var validName = Validation.Required("name", name);
        var values = prices.OrderBy(price => price.EffectiveFrom).ToImmutableArray();
        var validation = new ErrorListBuilder()
            .Add(Validation.Identifier(id.Value))
            .Add(Validation.Check(validName))
            .AddIf(values.IsEmpty, new ModelError.InvalidValue(), "prices")
            .AddIf(values.Select(price => price.EffectiveFrom).Distinct().Count() != values.Length,
                new CatalogueError.DuplicatePriceDate(), "prices")
            .ToResult();
        if (validation.IsFailure) return validation.Error;
        return new Service(id, validName.Value, duration, isConsultation, values[0]) { Prices = values };
    }

    public static Service SetPublic(this Service model, bool visible) => model with { IsPublic = visible };

    public static Service SetPublicName(this Service model, string? name) => model with { PublicNameOverride = Validation.OptionalText(name) };

    public static Result<Service, ErrorList> SetPublicPrice(this Service model, decimal amount)
    {
        var price = Money.Create(amount);
        return price.IsFailure ? price.Error : model with { PublicPrice = price.Value.Amount };
    }

    public static Service ChangeDuration(this Service model, LessonDuration duration) => model with { Duration = duration };

    public static Service Unarchive(this Service model) => model with { IsArchived = false };

    public static decimal? PriceAt(this Service model, DateTimeOffset instant) => model.Prices.Where(price => price.EffectiveFrom <= instant)
        .OrderByDescending(price => price.EffectiveFrom).Select(price => (decimal?)price.Amount).FirstOrDefault();

    public static Result<Service, ErrorList> SchedulePrice(this Service model, DateTimeOffset now, ServicePrice price)
    {
        var validation = new ErrorListBuilder()
            .AddIf(model.IsConsultation && price.Amount != 0, new ModelError.InvalidValue(), "amount")
            .AddIf(price.EffectiveFrom < now, new CatalogueError.PriceInPast(), "effectiveFrom")
            .AddIf(model.Prices.Any(previous => previous.EffectiveFrom == price.EffectiveFrom), new CatalogueError.DuplicatePriceDate(), "effectiveFrom")
            .ToResult();
        if (validation.IsFailure) return validation.Error;
        return model with { Prices = model.Prices.Add(price).OrderBy(value => value.EffectiveFrom).ToImmutableArray() };
    }

    public static Result<Service, ErrorList> Archive(this Service model, SchedulingUsage usage,
        IEnumerable<Payment> payments, IEnumerable<AppointmentSettlement> settlements)
    {
        var validation = Validation.Archive(usage);
        if (validation.IsFailure) return validation.Error;
        var purchases = payments.ToArray();
        var history = settlements.ToArray();
        if (purchases.Select(payment => payment.Id).Distinct().Count() != purchases.Length
            || history.Select(settlement => settlement.Id).Distinct().Count() != history.Length)
            return ErrorList.Single(new ModelError.Duplicate());
        if (purchases.Any(payment => payment.Conditions is null))
            return ErrorList.Single(new ModelError.DecisionRequired(), "paymentHistory");
        foreach (var payment in purchases.Where(payment => payment.Conditions!.ServiceId == model.Id))
        {
            if (history.Any(settlement => settlement.Source is SettlementSource.Prepaid source && source.Id == payment.Id
                && (settlement.ClientId != payment.ClientId || settlement.ServiceId != model.Id)))
                return ErrorList.Single(new FinanceError.InconsistentSettlement());
            var remaining = payment.RemainingLessons(history);
            if (remaining < 0) return ErrorList.Single(new FinanceError.InconsistentSettlement());
            if (remaining > 0) return ErrorList.Single(new ServiceArchiveError.UnusedPaidLessons());
        }
        return model with { IsArchived = true };
    }
}
