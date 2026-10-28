using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record SchedulingUsage(bool HasFutureAppointments, bool HasActiveSeries, bool HasHistoricalUse)
{
    public bool CanDelete => !HasFutureAppointments && !HasActiveSeries && !HasHistoricalUse;
}

public enum StaffRole { Superuser, Administrator, Teacher }

public sealed record Staff
{
    private Staff(StaffId id, string name, StaffRole role, bool participates)
    {
        Id = id; Name = name; Role = role; Participates = participates;
    }
    public StaffId Id { get; }
    public string Name { get; }
    public StaffRole Role { get; private init; }
    public bool Participates { get; private init; }
    public bool IsBlocked { get; private init; }
    public bool IsArchived { get; private init; }
    public bool ParticipatesInScheduling => Participates && !IsArchived;

    public static Result<Staff, ErrorList> Create(StaffId id, string? name, StaffRole role, bool participates)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new Staff(id, validName.Value, role, participates);
    }

    public Staff SetParticipation(bool participates) => this with { Participates = participates };
    public Staff Block() => this with { IsBlocked = true };
    public Staff Unblock() => this with { IsBlocked = false };
    public Staff ChangeRole(StaffRole role) => this with { Role = role };
    public Staff Unarchive() => this with { IsArchived = false };

    public Result<Staff, ErrorList> Archive(SchedulingUsage usage, bool isSoleTeacherOnUnfinishedCourse)
    {
        var validation = Validation.Collect(Validation.Archive(usage), isSoleTeacherOnUnfinishedCourse
            ? UnitResult.Failure(ErrorList.Of(new CatalogueError.SoleCourseTeacher())) : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return this with { IsArchived = true };
    }
}

public sealed record Room
{
    private Room(RoomId id, string name, string? comment) { Id = id; Name = name; Comment = comment; }
    public RoomId Id { get; }
    public string Name { get; }
    public string? Comment { get; }
    public bool IsArchived { get; private init; }

    public static Result<Room, ErrorList> Create(RoomId id, string? name, string? comment)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new Room(id, validName.Value, Validation.OptionalText(comment));
    }

    public Room Archive() => this with { IsArchived = true };
    public Room Unarchive() => this with { IsArchived = false };
}

public sealed record ServicePrice
{
    private ServicePrice(DateTimeOffset effectiveFrom, decimal amount) { EffectiveFrom = effectiveFrom; Amount = amount; }
    public DateTimeOffset EffectiveFrom { get; }
    public decimal Amount { get; }

    public static Result<ServicePrice, ErrorList> Create(DateTimeOffset effectiveFrom, decimal amount)
    {
        if (amount < 0) return ErrorList.Of(new CatalogueError.NegativePrice(), "amount");
        return new ServicePrice(effectiveFrom.ToUniversalTime(), amount);
    }
}

public sealed record Service
{
    private Service(ServiceId id, string name, LessonDuration duration, bool consultation, ServicePrice price)
    {
        Id = id; Name = name; Duration = duration; IsConsultation = consultation; Prices = [price];
    }
    public ServiceId Id { get; }
    public string Name { get; }
    public string PublicName => PublicNameOverride ?? Name;
    private string? PublicNameOverride { get; init; }
    public LessonDuration Duration { get; private init; }
    public bool IsConsultation { get; }
    public bool IsPublic { get; private init; }
    public bool IsArchived { get; private init; }
    public ImmutableArray<ServicePrice> Prices { get; private init; }

    public static Result<Service, ErrorList> Create(ServiceId id, string? name, LessonDuration duration, bool isConsultation, ServicePrice initialPrice)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new Service(id, validName.Value, duration, isConsultation, initialPrice);
    }

    public Service SetPublic(bool visible) => this with { IsPublic = visible };
    public Service SetPublicName(string? name) => this with { PublicNameOverride = Validation.OptionalText(name) };
    public Service ChangeDuration(LessonDuration duration) => this with { Duration = duration };
    public Service Unarchive() => this with { IsArchived = false };

    public decimal? PriceAt(DateTimeOffset instant) => Prices.Where(price => price.EffectiveFrom <= instant)
        .OrderByDescending(price => price.EffectiveFrom).Select(price => (decimal?)price.Amount).FirstOrDefault();

    public Result<Service, ErrorList> SchedulePrice(DateTimeOffset now, ServicePrice price)
    {
        var errors = new List<DomainIssue>();
        if (price.EffectiveFrom < now) errors.Add(new(new CatalogueError.PriceInPast(), "effectiveFrom"));
        if (Prices.Any(previous => previous.EffectiveFrom == price.EffectiveFrom)) errors.Add(new(new CatalogueError.DuplicatePriceDate(), "effectiveFrom"));
        if (errors.Count > 0) return ErrorList.From(errors);
        return this with { Prices = Prices.Add(price).OrderBy(value => value.EffectiveFrom).ToImmutableArray() };
    }

    public Result<Service, ErrorList> Archive(SchedulingUsage usage)
    {
        var validation = Validation.Archive(usage);
        if (validation.IsFailure) return validation.Error;
        return this with { IsArchived = true };
    }
}
