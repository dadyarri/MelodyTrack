using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence.Clients;

/// <summary>Хранит закрытую историю заметок отдельно от общего аудита.</summary>
public sealed class ClientNotesStore(Database database, ClientDataProtection protection) : IClientNotesStore
{
    /// <summary>Читает текущее защищённое поле клиента.</summary>
    public async Task<Result<string?, ErrorList>> GetCurrentAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        var row = await database.Query("SELECT protected_notes AS \"Notes\" FROM public.clients WHERE id = @id")
            .WithParameter("id", clientId.Value).GetSingleOrDefaultAsync<CurrentRow>(cancellationToken);
        return row is null ? ErrorList.Single(new ModelError.NotFound(), "clientId")
            : Result.Success<string?, ErrorList>(protection.UnprotectNotes(clientId, row.Notes));
    }

    /// <summary>Читает защищённые значения истории в устойчивом порядке.</summary>
    public async Task<Result<IReadOnlyList<ClientNotesChange>, ErrorList>> GetHistoryAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(clientId, cancellationToken);
        if (current.IsFailure) return current.Error;
        var rows = await database.Query("""
            SELECT id AS "Id", client_id AS "ClientId", author_staff_id AS "AuthorId", changed_at AS "ChangedAt",
                   protected_before AS "Before", protected_after AS "After"
            FROM public.client_notes_history WHERE client_id = @clientId ORDER BY changed_at, id
            """).WithParameter("clientId", clientId.Value).GetAllAsync<HistoryRow>(cancellationToken);
        var entries = new List<ClientNotesChange>(rows.Count);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = Restore(row);
            if (entry.IsFailure) return entry.Error;
            entries.Add(entry.Value);
        }
        return entries;
    }

    /// <summary>Ищет сохранённый результат команды по постоянному ключу без повторной записи.</summary>
    public async Task<Result<ClientNotesChange?, ErrorList>> FindChangeAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var row = await database.Query("""
            SELECT id AS "Id", client_id AS "ClientId", author_staff_id AS "AuthorId", changed_at AS "ChangedAt",
                   protected_before AS "Before", protected_after AS "After"
            FROM public.client_notes_history WHERE id = @id
            """).WithParameter("id", entryId).GetSingleOrDefaultAsync<HistoryRow>(cancellationToken);
        if (row is null) return Result.Success<ClientNotesChange?, ErrorList>(null);
        var entry = Restore(row);
        return entry.IsFailure ? entry.Error : Result.Success<ClientNotesChange?, ErrorList>(entry.Value);
    }

    /// <summary>Проверяет актуальное право, повтор и ожидаемое поле в одной транзакции до чтения закрытого текста.</summary>
    public Task<Result<ClientNotesChange, ErrorList>> ChangeAsync(ClientNotesWriteCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(null, command, cancellationToken);

    /// <summary>Проверяет актуальное право, сравнивает исходное поле и атомарно сохраняет текущее поле и историю.</summary>
    public Task<Result<ClientNotesChange, ErrorList>> AppendAsync(ClientNotesChange change, CancellationToken cancellationToken) =>
        ApplyAsync(change, null, cancellationToken);

    private async Task<Result<ClientNotesChange, ErrorList>> ApplyAsync(ClientNotesChange? change,
        ClientNotesWriteCommand? command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (change is null && command is null) return ErrorList.Single(new ModelError.InvalidValue());
        if (command is not null && (command.EntryId == Guid.Empty || command.ClientId.Value == Guid.Empty || command.AuthorId.Value == Guid.Empty))
            return ErrorList.Single(new IdentifierError.Empty());
        try { return await ApplyCoreAsync(change, command, cancellationToken); }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "client_notes_history_pkey")
        {
            return ErrorList.Single(new ModelError.InvalidState(), "id");
        }
    }

    private async Task<Result<ClientNotesChange, ErrorList>> ApplyCoreAsync(ClientNotesChange? supplied,
        ClientNotesWriteCommand? command, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var authorId = supplied?.AuthorId ?? command!.AuthorId;
        var clientId = supplied?.ClientId ?? command!.ClientId;
        var entryId = supplied?.Id ?? command!.EntryId;
        var selectedAt = supplied?.ChangedAt ?? command!.ChangedAt;
        var permission = await StaffPolicyStore.AuthorizeClientNotesWriteAsync(transaction, authorId, selectedAt, cancellationToken);
        if (permission.IsFailure) return permission.Error;
        var row = await transaction.Query("SELECT protected_notes AS \"Notes\" FROM public.clients WHERE id = @id FOR UPDATE")
            .WithParameter("id", clientId.Value).GetSingleOrDefaultAsync<CurrentRow>(cancellationToken);
        if (row is null) return ErrorList.Single(new ModelError.NotFound(), "clientId");
        var existing = await transaction.Query("""
            SELECT id AS "Id", client_id AS "ClientId", author_staff_id AS "AuthorId", changed_at AS "ChangedAt",
                   protected_before AS "Before", protected_after AS "After"
            FROM public.client_notes_history WHERE id = @id
            """).WithParameter("id", entryId).GetSingleOrDefaultAsync<HistoryRow>(cancellationToken);
        if (existing is not null)
        {
            var previous = Restore(existing);
            if (previous.IsFailure) return previous.Error;
            var matches = supplied is not null ? previous.Value == supplied
                : previous.Value.ClientId == clientId && previous.Value.AuthorId == authorId
                    && string.Equals(previous.Value.After, command!.Notes, StringComparison.Ordinal)
                    && (!command.HasExpectedState || string.Equals(previous.Value.Before, command.ExpectedNotes, StringComparison.Ordinal));
            return matches ? previous.Value : ErrorList.Single(new ModelError.InvalidState(), "id");
        }
        var before = protection.UnprotectNotes(clientId, row.Notes);
        if (supplied is not null && !string.Equals(before, supplied.Before, StringComparison.Ordinal)
            || command is { HasExpectedState: true } && !string.Equals(before, command.ExpectedNotes, StringComparison.Ordinal))
            return ErrorList.Single(new ModelError.InvalidState(), "notes");
        var candidate = supplied is not null ? Result.Success<ClientNotesChange, ErrorList>(supplied)
            : ClientNotesChange.Create(entryId, clientId, authorId, selectedAt, before, command!.Notes);
        if (candidate.IsFailure) return candidate.Error;
        var change = candidate.Value;
        await transaction.Query("UPDATE public.clients SET protected_notes = @notes WHERE id = @id")
            .WithParameter("id", change.ClientId.Value)
            .WithParameter("notes", protection.ProtectNotes(change.ClientId, change.After))
            .ExecuteAsync(cancellationToken);
        await transaction.Query("""
            INSERT INTO public.client_notes_history(id, client_id, author_staff_id, changed_at, protected_before, protected_after)
            VALUES (@id, @clientId, @authorId, @changedAt, @before, @after)
            """).WithParameter("id", change.Id).WithParameter("clientId", change.ClientId.Value)
            .WithParameter("authorId", change.AuthorId.Value).WithParameter("changedAt", change.ChangedAt)
            .WithParameter("before", protection.ProtectNotesBefore(change.ClientId, change.Id, change.Before))
            .WithParameter("after", protection.ProtectNotesAfter(change.ClientId, change.Id, change.After))
            .ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return change;
    }

    private Result<ClientNotesChange, ErrorList> Restore(HistoryRow row)
    {
        var clientId = new ClientId(row.ClientId);
        return ClientNotesChange.Create(row.Id, clientId, new(row.AuthorId), new DateTimeOffset(row.ChangedAt, TimeSpan.Zero),
            protection.UnprotectNotesBefore(clientId, row.Id, row.Before),
            protection.UnprotectNotesAfter(clientId, row.Id, row.After));
    }

    private sealed class CurrentRow
    {
        public string? Notes { get; init; }
    }

    private sealed class HistoryRow
    {
        public Guid Id { get; init; }
        public Guid ClientId { get; init; }
        public Guid AuthorId { get; init; }
        public DateTime ChangedAt { get; init; }
        public string? Before { get; init; }
        public string? After { get; init; }
    }
}
