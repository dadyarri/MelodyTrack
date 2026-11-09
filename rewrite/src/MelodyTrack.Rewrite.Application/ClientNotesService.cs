using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed record ClientNotesWriteCommand(Guid EntryId, ClientId ClientId, StaffId AuthorId, DateTimeOffset ChangedAt,
    string? Notes, string? ExpectedNotes, bool HasExpectedState)
{
    public override string ToString() => nameof(ClientNotesWriteCommand);
}

public interface IClientNotesStore
{
    Task<Result<string?, ErrorList>> GetCurrentAsync(ClientId clientId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ClientNotesChange>, ErrorList>> GetHistoryAsync(ClientId clientId, CancellationToken cancellationToken);
    Task<Result<ClientNotesChange?, ErrorList>> FindChangeAsync(Guid entryId, CancellationToken cancellationToken);
    // Rechecks current authority before reading/replaying text and atomically compares/appends the change.
    Task<Result<ClientNotesChange, ErrorList>> ChangeAsync(ClientNotesWriteCommand command, CancellationToken cancellationToken);
    // Low-level authorized append uses the same locked authority check and transaction as ChangeAsync.
    Task<Result<ClientNotesChange, ErrorList>> AppendAsync(ClientNotesChange change, CancellationToken cancellationToken);
}

public sealed class ClientNotesService(IClientNotesStore store)
{
    public Task<Result<ClientNotesChange, ErrorList>> ChangeAsync(ClientId clientId, Guid entryId,
        string? notes, DateTimeOffset now, Actor actor, CancellationToken cancellationToken) =>
        ChangeCoreAsync(clientId, entryId, notes, null, false, now, actor, cancellationToken);

    public Task<Result<ClientNotesChange, ErrorList>> ChangeExpectedAsync(ClientId clientId, Guid entryId,
        string? expectedNotes, string? notes, DateTimeOffset now, Actor actor, CancellationToken cancellationToken) =>
        ChangeCoreAsync(clientId, entryId, notes, expectedNotes, true, now, actor, cancellationToken);

    private async Task<Result<ClientNotesChange, ErrorList>> ChangeCoreAsync(ClientId clientId, Guid entryId,
        string? notes, string? expectedNotes, bool hasExpectation, DateTimeOffset now, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!actor.HasPermission(StaffPermission.ClientNotesWrite, recordAt: now, now: now) || actor is not Actor.Staff staff)
            return ErrorList.Single(new ModelError.Forbidden());
        if (clientId.Value == Guid.Empty || entryId == Guid.Empty)
            return ErrorList.Single(new IdentifierError.Empty());
        return await store.ChangeAsync(new(entryId, clientId, staff.Id, now, notes, expectedNotes, hasExpectation), cancellationToken);
    }

    public Task<Result<IReadOnlyList<ClientNotesChange>, ErrorList>> ReadHistoryAsync(ClientId clientId,
        Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = actor.Validate();
        if (validation.IsFailure) return Task.FromResult(Result.Failure<IReadOnlyList<ClientNotesChange>, ErrorList>(validation.Error));
        return actor.HasPermission(StaffPermission.ClientNotesRead) && clientId.Value != Guid.Empty
            ? store.GetHistoryAsync(clientId, cancellationToken)
            : Task.FromResult(Result.Failure<IReadOnlyList<ClientNotesChange>, ErrorList>(ErrorList.Single(new ModelError.Forbidden())));
    }

    public Task<Result<string?, ErrorList>> ReadCurrentAsync(ClientId clientId, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = actor.Validate();
        if (validation.IsFailure) return Task.FromResult(Result.Failure<string?, ErrorList>(validation.Error));
        return actor.HasPermission(StaffPermission.ClientNotesRead) && clientId.Value != Guid.Empty
            ? store.GetCurrentAsync(clientId, cancellationToken)
            : Task.FromResult(Result.Failure<string?, ErrorList>(ErrorList.Single(new ModelError.Forbidden())));
    }
}
