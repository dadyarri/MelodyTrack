using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public interface IExpenseStore
{
    Task<Result<Expense?, ErrorList>> FindAsync(ExpenseId id, CancellationToken cancellationToken);
    // Receives an authorized transition; atomically compares the original and appends subject history.
    Task<Result<ExpenseChange, ErrorList>> ApplyAsync(Guid commandId, ExpenseChange change, CancellationToken cancellationToken);
}
