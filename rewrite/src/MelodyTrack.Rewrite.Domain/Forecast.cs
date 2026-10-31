using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum ForecastUncertainty { SubscriptionValuation, PastAppointmentWithoutResult, MissingPrice, ReportingDateBoundary }
public sealed record ForecastLine(AppointmentId AppointmentId, Money? Value, ForecastUncertainty? Uncertainty);
public sealed record MonthlyForecast
{
    internal MonthlyForecast(int year, int month, ImmutableArray<ForecastLine> lines) { Year = year; Month = month; Lines = lines; }

    public int Year { get; }

    public int Month { get; }

    public ImmutableArray<ForecastLine> Lines { get; }
}
