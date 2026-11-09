using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

// Subject history may contain the correction reason; it must not be sent to a technical log.
public sealed record ExpenseChange(Expense? Before, Expense? After, StaffId AuthorId, DateTimeOffset OccurredAt, string? Comment);

public static class ExpenseOperations
{
    public static Result<ExpenseChange, ErrorList> Create(ExpenseId id, string? description, Money amount,
        DateOnly date, ExpenseCategoryId? categoryId, Actor actor, DateTimeOffset now)
    {
        var permission = CanAccess(actor, StaffPermission.ExpensesRecord, date, now);
        if (permission.IsFailure) return permission.Error;
        var created = ExpenseRules.Create(id, description, amount, date, categoryId);
        return created.IsFailure ? created.Error
            : new ExpenseChange(null, created.Value, ((Actor.Staff)actor).Id, now.ToUniversalTime(), null);
    }

    public static Result<ExpenseChange, ErrorList> Edit(this Expense model, string? description, Money amount,
        DateOnly date, ExpenseCategoryId? categoryId, string? comment, Actor actor, DateTimeOffset now)
    {
        // Moving the date must not turn a historical record into an editable one for the administrator.
        var permission = CanAccess(actor, StaffPermission.ExpensesCorrect, model.Date, now);
        if (permission.IsFailure) return permission.Error;
        permission = CanAccess(actor, StaffPermission.ExpensesCorrect, date, now);
        if (permission.IsFailure) return permission.Error;
        var reason = RequireCorrectionComment(actor, comment);
        if (reason.IsFailure) return reason.Error;
        var changed = ExpenseRules.Create(model.Id, description, amount, date, categoryId);
        return changed.IsFailure ? changed.Error
            : new ExpenseChange(model, changed.Value, ((Actor.Staff)actor).Id, now.ToUniversalTime(), comment?.Trim());
    }

    public static Result<ExpenseChange, ErrorList> Delete(this Expense model, string? comment, Actor actor, DateTimeOffset now)
    {
        var permission = CanAccess(actor, StaffPermission.ExpensesDelete, model.Date, now);
        if (permission.IsFailure) return permission.Error;
        var reason = RequireCorrectionComment(actor, comment);
        return reason.IsFailure ? reason.Error
            : new ExpenseChange(model, null, ((Actor.Staff)actor).Id, now.ToUniversalTime(), comment?.Trim());
    }

    public static Result<ImmutableArray<Expense>, ErrorList> List(IEnumerable<Expense> expenses,
        DateOnly from, DateOnly through, string? search, Actor actor, DateTimeOffset now)
    {
        if (from > through) return ErrorList.Single(new ModelError.InvalidValue(), "period");
        var permission = CanAccess(actor, StaffPermission.ExpensesRead, SchoolTime.Date(now), now);
        if (permission.IsFailure) return permission.Error;
        return expenses.Where(item => item.Date >= from && item.Date <= through
                && CanAccess(actor, StaffPermission.ExpensesRead, item.Date, now).IsSuccess
                && (string.IsNullOrWhiteSpace(search) || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id.Value).ToImmutableArray();
    }

    public static Result<ImmutableArray<Expense>, ErrorList> Export(IEnumerable<Expense> expenses, Actor actor, DateTimeOffset? now = null)
    {
        var valid = actor.Validate();
        if (valid.IsFailure) return valid.Error;
        if (!actor.HasPermission(StaffPermission.ExpensesExport, recordAt: now, now: now) ||
            !actor.HasPermission(StaffPermission.ExpensesRead, recordAt: now, now: now) ||
            actor is Actor.Staff { Role: StaffRole.Administrator } && now is null)
            return ErrorList.Single(new ModelError.Forbidden());
        return expenses.Where(item => CanAccess(actor, StaffPermission.ExpensesExport, item.Date, now).IsSuccess
            && CanAccess(actor, StaffPermission.ExpensesRead, item.Date, now).IsSuccess).ToImmutableArray();
    }

    private static UnitResult<ErrorList> CanAccess(Actor actor, StaffPermission permission, DateOnly date, DateTimeOffset? now)
    {
        var valid = actor.Validate();
        if (valid.IsFailure) return valid;
        var recordAt = SchoolTime.At(date, TimeOnly.MinValue);
        if (recordAt.IsFailure) return UnitResult.Failure(recordAt.Error);
        if (!actor.HasPermission(permission, recordAt: recordAt.Value, now: now) ||
            actor is Actor.Staff { Role: StaffRole.Administrator } && (now is not { } current || date != SchoolTime.Date(current)))
            return UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden()));
        return UnitResult.Success<ErrorList>();
    }

    private static UnitResult<ErrorList> RequireCorrectionComment(Actor actor, string? comment) =>
        actor is Actor.Staff { Role: StaffRole.Administrator } && string.IsNullOrWhiteSpace(comment)
            ? UnitResult.Failure(ErrorList.Single(new CatalogueError.Required("comment"), "comment"))
            : UnitResult.Success<ErrorList>();
}
