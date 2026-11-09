using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record SchedulingUsage(bool HasFutureAppointments, bool HasActiveSeries, bool HasHistoricalUse)
{

}

public enum StaffRole { Superuser, Administrator, Teacher }

public sealed record Staff
{
    internal Staff(StaffId id, string name, StaffRole role, bool participates)
    {
        Id = id; Name = name; Role = role; Participates = participates;
    }

    public StaffId Id { get; }

    public string Name { get; }

    public StaffRole Role { get; internal init; }

    public bool Participates { get; internal init; }

    public bool IsBlocked { get; internal init; }

    public bool IsArchived { get; internal init; }
}

public sealed record Room
{
    internal Room(RoomId id, string name, string? comment) { Id = id; Name = name; Comment = comment; }

    public RoomId Id { get; }

    public string Name { get; }

    public string? Comment { get; }

    public bool IsArchived { get; internal init; }
}

public sealed record ServicePrice
{
    private ServicePrice(DateTimeOffset effectiveFrom, decimal amount) { EffectiveFrom = effectiveFrom; Amount = amount; }
    public DateTimeOffset EffectiveFrom { get; }
    public decimal Amount { get; }

    public static Result<ServicePrice, ErrorList> Create(DateTimeOffset effectiveFrom, decimal amount)
    {
        if (amount < 0) return ErrorList.Single(new CatalogueError.NegativePrice(), "amount");
        if (decimal.Round(amount, 2) != amount) return ErrorList.Single(new ModelError.InvalidValue(), "amount");
        return new ServicePrice(effectiveFrom.ToUniversalTime(), amount);
    }
}

public sealed record Service
{
    internal Service(ServiceId id, string name, LessonDuration duration, bool consultation, ServicePrice price)
    {
        Id = id; Name = name; Duration = duration; IsConsultation = consultation; Prices = [price];
    }

    public ServiceId Id { get; }

    public string Name { get; }

    public string PublicName => PublicNameOverride ?? Name;

    internal string? PublicNameOverride { get; init; }

    public LessonDuration Duration { get; internal init; }

    public bool IsConsultation { get; }

    public bool IsPublic { get; internal init; }

    public decimal? PublicPrice { get; internal init; }

    public bool IsArchived { get; internal init; }

    public ImmutableArray<ServicePrice> Prices { get; internal init; }
}
