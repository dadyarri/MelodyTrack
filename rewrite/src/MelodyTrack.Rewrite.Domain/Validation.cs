using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record DomainIssue(DomainError Error, string? Field = null);

public sealed class ErrorList
{
    private ErrorList(ImmutableArray<DomainIssue> issues) => Issues = issues;

    internal ErrorList(DomainIssue first, IEnumerable<DomainIssue> remaining) : this([first, .. remaining]) { }

    public ImmutableArray<DomainIssue> Issues { get; }

    public static ErrorList Single(DomainError error, string? field = null) => new([new(error, field)]);
}

public sealed class ErrorListBuilder
{
    private readonly List<DomainIssue> _issues = [];

    public ErrorListBuilder Add(DomainError error, string? field = null)
    {
        _issues.Add(new DomainIssue(error, field));
        return this;
    }

    public ErrorListBuilder AddIf(bool condition, DomainError error, string? field = null)
    {
        if (condition) Add(error, field);
        return this;
    }

    public ErrorListBuilder AddRange(IEnumerable<DomainIssue> issues)
    {
        _issues.AddRange(issues);
        return this;
    }

    public ErrorListBuilder Add(UnitResult<ErrorList> check)
    {
        if (check.IsFailure) AddRange(check.Error.Issues);
        return this;
    }

    public UnitResult<ErrorList> ToResult() => _issues.Count == 0
        ? UnitResult.Success<ErrorList>()
        : UnitResult.Failure(new ErrorList(_issues[0], _issues.Skip(1)));
}

internal static class Validation
{
    internal static Result<string, ErrorList> Required(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ErrorList.Single(new CatalogueError.Required(field), field);
        }
        return value.Trim();
    }

    internal static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static UnitResult<ErrorList> Identifier(Guid value, string field = "id") => value == Guid.Empty
        ? UnitResult.Failure(ErrorList.Single(new IdentifierError.Empty(), field))
        : UnitResult.Success<ErrorList>();

    internal static UnitResult<ErrorList> Collect(params UnitResult<ErrorList>[] checks)
    {
        var errors = new ErrorListBuilder();
        foreach (var check in checks) errors.Add(check);
        return errors.ToResult();
    }

    internal static UnitResult<ErrorList> Check<T>(Result<T, ErrorList> result) => result.IsFailure
        ? UnitResult.Failure(result.Error)
        : UnitResult.Success<ErrorList>();

    internal static UnitResult<ErrorList> Archive(SchedulingUsage usage)
    {
        return new ErrorListBuilder()
            .AddIf(usage.HasFutureAppointments, new CatalogueError.FutureAppointmentsExist())
            .AddIf(usage.HasActiveSeries, new CatalogueError.ActiveSeriesExist())
            .ToResult();
    }
}
