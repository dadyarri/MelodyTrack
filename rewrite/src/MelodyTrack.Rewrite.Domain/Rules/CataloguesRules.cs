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
        return new Service(id, validName.Value, duration, isConsultation, initialPrice);
    }

    public static Service SetPublic(this Service model, bool visible) => model with { IsPublic = visible };

    public static Service SetPublicName(this Service model, string? name) => model with { PublicNameOverride = Validation.OptionalText(name) };

    public static Service ChangeDuration(this Service model, LessonDuration duration) => model with { Duration = duration };

    public static Service Unarchive(this Service model) => model with { IsArchived = false };

    public static decimal? PriceAt(this Service model, DateTimeOffset instant) => model.Prices.Where(price => price.EffectiveFrom <= instant)
        .OrderByDescending(price => price.EffectiveFrom).Select(price => (decimal?)price.Amount).FirstOrDefault();

    public static Result<Service, ErrorList> SchedulePrice(this Service model, DateTimeOffset now, ServicePrice price)
    {
        var validation = new ErrorListBuilder()
            .AddIf(price.EffectiveFrom < now, new CatalogueError.PriceInPast(), "effectiveFrom")
            .AddIf(model.Prices.Any(previous => previous.EffectiveFrom == price.EffectiveFrom), new CatalogueError.DuplicatePriceDate(), "effectiveFrom")
            .ToResult();
        if (validation.IsFailure) return validation.Error;
        return model with { Prices = model.Prices.Add(price).OrderBy(value => value.EffectiveFrom).ToImmutableArray() };
    }

    public static Result<Service, ErrorList> Archive(this Service model, SchedulingUsage usage)
    {
        var validation = Validation.Archive(usage);
        if (validation.IsFailure) return validation.Error;
        return model with { IsArchived = true };
    }
}
