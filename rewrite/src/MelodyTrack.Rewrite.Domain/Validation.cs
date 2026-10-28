using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Scheduling;

public sealed record DomainIssue(DomainError Error, string? Field = null);

public sealed class ErrorList
{
    private ErrorList(ImmutableArray<DomainIssue> issues) => Issues = issues;

    public ImmutableArray<DomainIssue> Issues { get; }

    public static ErrorList Of(DomainError error, string? field = null) => new([new(error, field)]);

    public static ErrorList From(IEnumerable<DomainIssue> issues)
    {
        var values = issues.ToImmutableArray();
        if (values.IsEmpty)
        {
            throw new ArgumentException("Список ошибок должен быть непустым.", nameof(issues));
        }
        return new(values);
    }

    public static ErrorList Combine(IEnumerable<ErrorList> errors) => From(errors.SelectMany(error => error.Issues));
}

internal static class Validation
{
    internal static Result<string, ErrorList> Required(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ErrorList.Of(new CatalogueError.Required(field), field);
        }
        return value.Trim();
    }

    internal static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static UnitResult<ErrorList> Identifier(Guid value, string field = "id") => value == Guid.Empty
        ? UnitResult.Failure(ErrorList.Of(new IdentifierError.Empty(), field))
        : UnitResult.Success<ErrorList>();

    internal static UnitResult<ErrorList> Collect(params UnitResult<ErrorList>[] checks) => Result.Combine<ErrorList>(checks, ErrorList.Combine);

    internal static UnitResult<ErrorList> Check<T>(Result<T, ErrorList> result) => result.IsFailure
        ? UnitResult.Failure(result.Error)
        : UnitResult.Success<ErrorList>();

    internal static UnitResult<ErrorList> Archive(SchedulingUsage usage)
    {
        var errors = new List<DomainIssue>();
        if (usage.HasFutureAppointments) errors.Add(new(new CatalogueError.FutureAppointmentsExist()));
        if (usage.HasActiveSeries) errors.Add(new(new CatalogueError.ActiveSeriesExist()));
        return errors.Count == 0 ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.From(errors));
    }
}
