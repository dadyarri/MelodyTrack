using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum ForecastUncertainty { MissingPrice, PaidReserveValuation }
public closed record ForecastLine(AppointmentId AppointmentId)
{
    public sealed record Known(AppointmentId AppointmentId, Money Value) : ForecastLine(AppointmentId);
    public sealed record Unresolved(AppointmentId AppointmentId, ForecastUncertainty Reason) : ForecastLine(AppointmentId);
}
public sealed record MonthlyForecast
{
    internal MonthlyForecast(int year, int month, ImmutableArray<ForecastLine> lines) { Year = year; Month = month; Lines = lines; }

    public int Year { get; }

    public int Month { get; }

    public ImmutableArray<ForecastLine> Lines { get; }
}
