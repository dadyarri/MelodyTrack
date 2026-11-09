using CSharpFunctionalExtensions;
using System.Globalization;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence.Finance;

/// <summary>Persists an authorized expense transition and its subject history in one transaction.</summary>
public sealed class ExpenseStore(Database database) : IExpenseStore
{
    private const string SelectExpense = """
        SELECT id AS "Id", description AS "Description", amount AS "Amount", to_char(date, 'YYYY-MM-DD') AS "Date", category_id AS "CategoryId"
        FROM public.expenses WHERE id = @id AND NOT is_deleted
        """;
    private const string SelectChange = """
        SELECT expense_id AS "Id", author_staff_id AS "AuthorId", occurred_at AS "OccurredAt", comment AS "Comment",
               before_description AS "BeforeDescription", before_amount AS "BeforeAmount", to_char(before_date, 'YYYY-MM-DD') AS "BeforeDate", before_category_id AS "BeforeCategoryId",
               after_description AS "AfterDescription", after_amount AS "AfterAmount", to_char(after_date, 'YYYY-MM-DD') AS "AfterDate", after_category_id AS "AfterCategoryId"
        FROM public.expense_changes WHERE command_id = @commandId
        """;

    /// <summary>Finds an active expense; authorization belongs to the application boundary.</summary>
    public async Task<Result<Expense?, ErrorList>> FindAsync(ExpenseId id, CancellationToken cancellationToken)
    {
        var row = await database.Query(SelectExpense).WithParameter("id", id.Value).GetSingleOrDefaultAsync<ExpenseRow>(cancellationToken);
        return row is null ? (Expense?)null : Restore(row.Id, row.Description, row.Amount, row.Date, row.CategoryId);
    }

    /// <summary>Serializes writes to one expense and safely replays an identical command.</summary>
    public async Task<Result<ExpenseChange, ErrorList>> ApplyAsync(Guid commandId, ExpenseChange change, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandId == Guid.Empty || change.AuthorId.Value == Guid.Empty)
            return ErrorList.Single(new IdentifierError.Empty());
        if (change.Before is null && change.After is null || change.Before is not null && change.After is not null && change.Before.Id != change.After.Id)
            return ErrorList.Single(new ModelError.InvalidValue(), "expense");
        try { return await ApplyCoreAsync(commandId, change, cancellationToken); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation && error.ConstraintName == "expense_changes_pkey")
        { return ErrorList.Single(new ModelError.InvalidState(), "commandId"); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation && error.ConstraintName == "expenses_category_id_fkey")
        { return ErrorList.Single(new ModelError.InvalidValue(), "categoryId"); }
    }

    private async Task<Result<ExpenseChange, ErrorList>> ApplyCoreAsync(Guid commandId, ExpenseChange change, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var dates = new List<DateTimeOffset>();
        foreach (var expense in new[] { change.Before, change.After })
        {
            if (expense is null) continue;
            var at = SchoolTime.At(expense.Date, TimeOnly.MinValue);
            if (at.IsFailure) return at.Error;
            dates.Add(at.Value);
        }
        var permission = change.Before is null ? StaffPermission.ExpensesRecord
            : change.After is null ? StaffPermission.ExpensesDelete : StaffPermission.ExpensesCorrect;
        var authorized = await StaffPolicyStore.AuthorizeWriteAsync(transaction, change.AuthorId, permission, dates, cancellationToken, change.Comment);
        if (authorized.IsFailure) return authorized.Error;
        var replay = await transaction.Query(SelectChange).WithParameter("commandId", commandId).GetSingleOrDefaultAsync<ChangeRow>(cancellationToken);
        if (replay is not null) return Replay(replay, change);
        var id = (change.After ?? change.Before)!.Id;
        if (change.Before is null)
        {
            var inserted = await WithExpense(transaction.Query("""
                INSERT INTO public.expenses(id, description, amount, date, category_id)
                VALUES (@id, @description, @amount, CAST(@date AS date), @categoryId) ON CONFLICT (id) DO NOTHING RETURNING id
                """), change.After!).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
            if (inserted is null)
            {
                replay = await transaction.Query(SelectChange).WithParameter("commandId", commandId).GetSingleOrDefaultAsync<ChangeRow>(cancellationToken);
                return replay is not null ? Replay(replay, change) : ErrorList.Single(new ModelError.Duplicate(), "id");
            }
        }
        else
        {
            var current = await transaction.Query(SelectExpense + " FOR UPDATE").WithParameter("id", id.Value).GetSingleOrDefaultAsync<ExpenseRow>(cancellationToken);
            replay = await transaction.Query(SelectChange).WithParameter("commandId", commandId).GetSingleOrDefaultAsync<ChangeRow>(cancellationToken);
            if (replay is not null) return Replay(replay, change);
            if (current is null) return ErrorList.Single(new ModelError.NotFound(), "expense");
            var restored = Restore(current.Id, current.Description, current.Amount, current.Date, current.CategoryId);
            if (restored.IsFailure) return restored.Error;
            if (restored.Value != change.Before) return ErrorList.Single(new ModelError.InvalidState(), "expense");
            if (change.After is { } after)
                await WithExpense(transaction.Query("UPDATE public.expenses SET description=@description, amount=@amount, date=CAST(@date AS date), category_id=@categoryId WHERE id=@id"), after).ExecuteAsync(cancellationToken);
            else
                await transaction.Query("UPDATE public.expenses SET is_deleted=true WHERE id=@id").WithParameter("id", id.Value).ExecuteAsync(cancellationToken);
        }
        await transaction.Query("""
            INSERT INTO public.expense_changes(command_id, expense_id, author_staff_id, occurred_at, comment,
                before_description, before_amount, before_date, before_category_id, after_description, after_amount, after_date, after_category_id)
            VALUES (@commandId, @id, @authorId, @occurredAt, @comment, @beforeDescription, @beforeAmount, CAST(@beforeDate AS date), @beforeCategoryId,
                @afterDescription, @afterAmount, CAST(@afterDate AS date), @afterCategoryId)
            """).WithParameter("commandId", commandId).WithParameter("id", id.Value).WithParameter("authorId", change.AuthorId.Value)
            .WithParameter("occurredAt", change.OccurredAt.ToUniversalTime()).WithParameter("comment", change.Comment)
            .WithParameter("beforeDescription", change.Before?.Description).WithParameter("beforeAmount", change.Before?.Amount.Amount)
            .WithParameter("beforeDate", DateText(change.Before?.Date)).WithParameter("beforeCategoryId", change.Before?.CategoryId?.Value)
            .WithParameter("afterDescription", change.After?.Description).WithParameter("afterAmount", change.After?.Amount.Amount)
            .WithParameter("afterDate", DateText(change.After?.Date)).WithParameter("afterCategoryId", change.After?.CategoryId?.Value)
            .ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return change;
    }

    private static DatabaseQuery WithExpense(DatabaseQuery query, Expense expense) => query.WithParameter("id", expense.Id.Value)
        .WithParameter("description", expense.Description).WithParameter("amount", expense.Amount.Amount)
        .WithParameter("date", DateText(expense.Date)).WithParameter("categoryId", expense.CategoryId?.Value);

    private static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Result<Expense?, ErrorList> Restore(Guid id, string? description, decimal? amount, string? date, Guid? categoryId)
    {
        if (description is null) return (Expense?)null;
        if (amount is null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            return ErrorList.Single(new ModelError.InvalidState());
        var money = Money.Create(amount.Value);
        if (money.IsFailure) return money.Error;
        var result = ExpenseRules.Create(new(id), description, money.Value, parsedDate, categoryId is { } category ? new ExpenseCategoryId(category) : null);
        return result.IsFailure ? result.Error : result.Value;
    }

    private static Result<ExpenseChange, ErrorList> Replay(ChangeRow row, ExpenseChange requested)
    {
        var before = Restore(row.Id, row.BeforeDescription, row.BeforeAmount, row.BeforeDate, row.BeforeCategoryId);
        var after = Restore(row.Id, row.AfterDescription, row.AfterAmount, row.AfterDate, row.AfterCategoryId);
        if (before.IsFailure) return before.Error;
        if (after.IsFailure) return after.Error;
        var previous = new ExpenseChange(before.Value, after.Value, new(row.AuthorId), new DateTimeOffset(row.OccurredAt, TimeSpan.Zero), row.Comment);
        return previous.Before == requested.Before && previous.After == requested.After && previous.AuthorId == requested.AuthorId
            && previous.Comment == requested.Comment
            ? previous : ErrorList.Single(new ModelError.InvalidState(), "commandId");
    }

    private sealed class ExpenseRow
    {
        public Guid Id { get; init; }
        public string Description { get; init; } = "";
        public decimal Amount { get; init; }
        public string Date { get; init; } = "";
        public Guid? CategoryId { get; init; }
    }

    private sealed class ChangeRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public DateTime OccurredAt { get; init; }
        public string? Comment { get; init; }
        public string? BeforeDescription { get; init; }
        public decimal? BeforeAmount { get; init; }
        public string? BeforeDate { get; init; }
        public Guid? BeforeCategoryId { get; init; }
        public string? AfterDescription { get; init; }
        public decimal? AfterAmount { get; init; }
        public string? AfterDate { get; init; }
        public Guid? AfterCategoryId { get; init; }
    }
}
