using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed record ExpenseValues(string? Description, decimal Amount, DateOnly Date, Guid? CategoryId);

public sealed class ExpenseService(IExpenseStore store)
{
    public async Task<Result<ExpenseChange, ErrorList>> CreateAsync(Guid commandId, ExpenseValues values,
        Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandId == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty(), "commandId");
        var money = Money.Create(values.Amount);
        if (money.IsFailure) return money.Error;
        // A stable server-selected expense identity makes a retried POST address the same record.
        var change = ExpenseOperations.Create(new(commandId), values.Description, money.Value, values.Date,
            Category(values), actor, now);
        return change.IsFailure ? change.Error : await store.ApplyAsync(commandId, change.Value, cancellationToken);
    }

    public async Task<Result<ExpenseChange, ErrorList>> EditAsync(ExpenseId id, Guid commandId,
        ExpenseValues expected, ExpenseValues values, string? comment, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id.Value == Guid.Empty || commandId == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty());
        var beforeMoney = Money.Create(expected.Amount);
        if (beforeMoney.IsFailure) return beforeMoney.Error;
        var before = ExpenseRules.Create(id, expected.Description, beforeMoney.Value, expected.Date, Category(expected));
        if (before.IsFailure) return before.Error;
        var money = Money.Create(values.Amount);
        if (money.IsFailure) return money.Error;
        var change = before.Value.Edit(values.Description, money.Value, values.Date, Category(values), comment, actor, now);
        // Store compares the supplied original under lock, including when a later correction already exists.
        return change.IsFailure ? change.Error : await store.ApplyAsync(commandId, change.Value, cancellationToken);
    }

    private static ExpenseCategoryId? Category(ExpenseValues values) => values.CategoryId is { } id ? new(id) : null;
}
