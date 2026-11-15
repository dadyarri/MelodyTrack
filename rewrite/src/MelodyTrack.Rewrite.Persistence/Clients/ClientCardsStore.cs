using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence.Access;

namespace MelodyTrack.Rewrite.Persistence.Clients;

/// <summary>Reads and changes client cards within current-policy checked transactions.</summary>
public sealed class ClientCardsStore(Database database, ClientDataProtection protection) : IClientCardsStore
{
    /// <summary>Reads a client card and its protected contacts from one consistent snapshot.</summary>
    public async Task<Result<ClientCard, ErrorList>> ReadAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        await transaction.Query("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY").ExecuteAsync(cancellationToken);
        var card = await ReadCardAsync(transaction, clientId, cancellationToken);
        if (card.IsSuccess) await transaction.CommitAsync(cancellationToken);
        return card;
    }

    /// <summary>Creates a card and stores a replay receipt atomically with its duplicate warning.</summary>
    public async Task<Result<ClientCardCommandResult, ErrorList>> CreateAsync(Guid commandId, StaffId authorId,
        ClientId clientId, ClientCardValues values, byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var authorized = await StaffPolicyStore.AuthorizeClientWriteAsync(transaction, authorId, now, cancellationToken);
        if (authorized.IsFailure) return authorized.Error;
        await LockAsync(transaction, "command:" + commandId.ToString("N"), cancellationToken);
        var replay = await ReadCommandAsync(transaction, commandId, authorId, clientId, "create", requestFingerprint, cancellationToken);
        if (replay.IsFailure) return replay.Error;
        if (replay.Value is { } prior) return new ClientCardCommandResult(prior.Client!, prior.HasDuplicateContacts);
        var valid = await ValidateInputsAsync(transaction, clientId, values, [], cancellationToken);
        if (valid.IsFailure) return valid.Error;
        await LockAsync(transaction, "client:" + clientId.Value.ToString("N"), cancellationToken);
        var exists = await transaction.Query("SELECT EXISTS(SELECT 1 FROM public.clients WHERE id=@id)")
            .WithParameter("id", clientId.Value).GetSingleAsync<bool>(cancellationToken);
        if (exists) return ErrorList.Single(new ModelError.InvalidState(), "clientId");
        await transaction.Query("""
            INSERT INTO public.clients(id, first_name, last_name, middle_name, birthdate, source_id, created_at)
            VALUES (@id, @first, @last, @middle, @birthdate, @source, @createdAt)
            """).WithParameter("id", clientId.Value).WithParameter("first", values.FirstName.Trim())
            .WithParameter("last", values.LastName.Trim()).WithParameter("middle", values.MiddleName?.Trim())
            .WithParameter("birthdate", values.Birthdate).WithParameter("source", values.SourceId)
            .WithParameter("createdAt", now).ExecuteAsync(cancellationToken);
        await ReplaceContactsAsync(transaction, clientId, values.Contacts, cancellationToken);
        var card = await ReadCardAsync(transaction, clientId, cancellationToken);
        if (card.IsFailure) return card.Error;
        var duplicates = await HasDuplicatesAsync(transaction, clientId, values, cancellationToken);
        await SaveCommandAsync(transaction, commandId, authorId, clientId, "create", requestFingerprint, duplicates, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ClientCardCommandResult(card.Value, duplicates);
    }

    /// <summary>Compares the expected card state and replaces profile and contacts atomically.</summary>
    public async Task<Result<ClientCardCommandResult, ErrorList>> UpdateAsync(Guid commandId, StaffId authorId,
        ClientId clientId, ClientCardExpectedState expectedState, ClientCardValues values, byte[] requestFingerprint,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var authorized = await StaffPolicyStore.AuthorizeClientWriteAsync(transaction, authorId, now, cancellationToken);
        if (authorized.IsFailure) return authorized.Error;
        await LockAsync(transaction, "command:" + commandId.ToString("N"), cancellationToken);
        var replay = await ReadCommandAsync(transaction, commandId, authorId, clientId, "update", requestFingerprint, cancellationToken);
        if (replay.IsFailure) return replay.Error;
        if (replay.Value is { } prior) return new ClientCardCommandResult(null, prior.HasDuplicateContacts);
        var current = await ReadCardAsync(transaction, clientId, cancellationToken, forUpdate: true);
        if (current.IsFailure) return current.Error;
        if (!SameValues(expectedState.Values, ToValues(current.Value)))
            return ErrorList.Single(new ModelError.InvalidState(), "expectedState");
        var valid = await ValidateInputsAsync(transaction, clientId, values, current.Value.Contacts, cancellationToken);
        if (valid.IsFailure) return valid.Error;
        await transaction.Query("""
            UPDATE public.clients SET first_name=@first, last_name=@last, middle_name=@middle, birthdate=@birthdate, source_id=@source
            WHERE id=@id
            """).WithParameter("id", clientId.Value).WithParameter("first", values.FirstName.Trim())
            .WithParameter("last", values.LastName.Trim()).WithParameter("middle", values.MiddleName?.Trim())
            .WithParameter("birthdate", values.Birthdate).WithParameter("source", values.SourceId).ExecuteAsync(cancellationToken);
        await ReplaceContactsAsync(transaction, clientId, values.Contacts, cancellationToken);
        var duplicates = await HasDuplicatesAsync(transaction, clientId, values, cancellationToken);
        await SaveCommandAsync(transaction, commandId, authorId, clientId, "update", requestFingerprint, duplicates, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ClientCardCommandResult(null, duplicates);
    }

    /// <summary>Restores an archived card without changing revoked client access credentials.</summary>
    public async Task<UnitResult<ErrorList>> RestoreAsync(Guid commandId, StaffId authorId, ClientId clientId,
        byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var authorized = await StaffPolicyStore.AuthorizeClientWriteAsync(transaction, authorId, now, cancellationToken);
        if (authorized.IsFailure) return authorized;
        await LockAsync(transaction, "command:" + commandId.ToString("N"), cancellationToken);
        var replay = await ReadCommandAsync(transaction, commandId, authorId, clientId, "restore", requestFingerprint, cancellationToken);
        if (replay.IsFailure) return replay.Error;
        var wasApplied = await transaction.Query("SELECT EXISTS(SELECT 1 FROM public.client_card_commands WHERE command_id=@id)")
            .WithParameter("id", commandId).GetSingleAsync<bool>(cancellationToken);
        if (wasApplied) return UnitResult.Success<ErrorList>();
        var changed = await transaction.Query("UPDATE public.clients SET is_archived=false WHERE id=@id AND is_archived=true RETURNING id")
            .WithParameter("id", clientId.Value).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (changed is null) return ErrorList.Single(new ModelError.InvalidState(), "clientId");
        await SaveCommandAsync(transaction, commandId, authorId, clientId, "restore", requestFingerprint, false, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UnitResult.Success<ErrorList>();
    }

    private async Task<Result<ClientCard, ErrorList>> ReadCardAsync(DatabaseTransaction transaction, ClientId clientId,
        CancellationToken cancellationToken, bool forUpdate = false)
    {
        var lockClause = forUpdate ? " FOR UPDATE OF c" : "";
        var row = await transaction.Query($"""
            SELECT c.id AS "Id", c.first_name AS "FirstName", c.last_name AS "LastName", c.middle_name AS "MiddleName",
                c.birthdate::text AS "Birthdate", c.source_id AS "SourceId", s.name AS "SourceName", c.is_archived AS "IsArchived", c.created_at AS "CreatedAt"
            FROM public.clients c LEFT JOIN public.client_sources s ON s.id=c.source_id WHERE c.id=@id{lockClause}
            """).WithParameter("id", clientId.Value).GetSingleOrDefaultAsync<CardRow>(cancellationToken);
        if (row is null) return ErrorList.Single(new ModelError.NotFound(), "clientId");
        var contacts = await transaction.Query("""
            SELECT c.id AS "Id", c.kind_id AS "KindId", k.name AS "KindName", k.format::text AS "Format",
                c.protected_value AS "Value", c.protected_comment AS "Comment", c.is_primary AS "IsPrimary"
            FROM public.client_contacts c JOIN public.contact_kinds k ON k.id=c.kind_id WHERE c.client_id=@id ORDER BY k.name,c.id
            """).WithParameter("id", clientId.Value).GetAllAsync<ContactRow>(cancellationToken);
        var items = contacts.Select(value => new ClientCardContact(value.Id, value.KindId, value.KindName, value.Format,
            protection.UnprotectValue(clientId, new(value.Id), value.Value), protection.UnprotectComment(clientId, new(value.Id), value.Comment), value.IsPrimary)).ToArray();
        return new ClientCard(row.Id, row.FirstName, row.LastName, row.MiddleName,
            row.Birthdate is null ? null : DateOnly.ParseExact(row.Birthdate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.SourceId, row.SourceName, row.IsArchived, new DateTimeOffset(row.CreatedAt, TimeSpan.Zero), items);
    }

    private async Task<UnitResult<ErrorList>> ValidateInputsAsync(DatabaseTransaction tx, ClientId clientId,
        ClientCardValues values, IReadOnlyList<ClientCardContact> existing, CancellationToken token)
    {
        var source = await tx.Query("SELECT EXISTS(SELECT 1 FROM public.client_sources WHERE id=@id)")
            .WithParameter("id", values.SourceId).GetSingleAsync<bool>(token);
        if (!source) return ErrorList.Single(new ModelError.InvalidValue(), "sourceId");
        var ids = values.Contacts.Select(item => item.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length || ids.Any(id => id == Guid.Empty))
            return ErrorList.Single(new ClientError.DuplicateContact(), "contacts");
        foreach (var id in ids.Order()) await LockAsync(tx, "contact:" + id.ToString("N"), token);
        foreach (var contact in values.Contacts)
        {
            var claimed = await tx.Query("SELECT EXISTS(SELECT 1 FROM public.client_contacts WHERE id=@id AND client_id<>@client)")
                .WithParameter("id", contact.Id).WithParameter("client", clientId.Value).GetSingleAsync<bool>(token);
            if (claimed) return ErrorList.Single(new ModelError.InvalidState(), "contacts.id");
            var kind = await tx.Query("SELECT name AS \"Name\", format::text AS \"Format\", is_archived AS \"IsArchived\" FROM public.contact_kinds WHERE id=@id")
                .WithParameter("id", contact.KindId).GetSingleOrDefaultAsync<KindRow>(token);
            if (kind is null) return ErrorList.Single(new ModelError.InvalidValue(), "contacts.kindId");
            var prior = existing.FirstOrDefault(value => value.Id == contact.Id);
            if (prior is not null && prior.KindId == contact.KindId && prior.Value == contact.Value) continue;
            if (kind.IsArchived) return ErrorList.Single(new ClientError.ContactKindArchived(), "contacts.kindId");
            var format = kind.Format switch { "phone" => ContactFormat.Phone, "email" => ContactFormat.Email, "link" => ContactFormat.Link, _ => (ContactFormat)(-1) };
            var contactKind = ContactKindRules.Create(new(contact.KindId), kind.Name, format);
            if (contactKind.IsFailure) return ErrorList.Single(new ModelError.InvalidValue(), "contacts.kindId");
            var valid = ClientContactRules.Create(new(contact.Id), contactKind.Value, contact.Value, contact.Comment, contact.IsPrimary);
            if (valid.IsFailure) return ErrorList.Single(new ClientError.InvalidContactValue(), "contacts.value");
        }
        foreach (var group in values.Contacts.GroupBy(item => item.KindId))
            if (group.Count(item => item.IsPrimary) != 1) return ErrorList.Single(new ClientError.PrimaryContactRequired(new(group.Key)), "contacts");
        return UnitResult.Success<ErrorList>();
    }

    private async Task ReplaceContactsAsync(DatabaseTransaction tx, ClientId clientId, IReadOnlyList<ClientContactInput> contacts, CancellationToken token)
    {
        await tx.Query("DELETE FROM public.client_contacts WHERE client_id=@id").WithParameter("id", clientId.Value).ExecuteAsync(token);
        foreach (var contact in contacts)
            await tx.Query("""
                INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
                VALUES (@id,@client,@kind,@value,@comment,@primary)
                """).WithParameter("id", contact.Id).WithParameter("client", clientId.Value).WithParameter("kind", contact.KindId)
                .WithParameter("value", protection.ProtectValue(clientId, new(contact.Id), contact.Value))
                .WithParameter("comment", protection.ProtectComment(clientId, new(contact.Id), contact.Comment))
                .WithParameter("primary", contact.IsPrimary).ExecuteAsync(token);
    }

    private async Task<bool> HasDuplicatesAsync(DatabaseTransaction tx, ClientId clientId, ClientCardValues values, CancellationToken token)
    {
        var rows = await tx.Query("""
            SELECT c.client_id AS "ClientId", k.format::text AS "Format", c.protected_value AS "Value", c.id AS "Id"
            FROM public.client_contacts c JOIN public.contact_kinds k ON k.id=c.kind_id WHERE c.client_id<>@id
            """).WithParameter("id", clientId.Value).GetAllAsync<DuplicateRow>(token);
        foreach (var input in values.Contacts)
        {
            var kind = await tx.Query("SELECT format::text AS \"Format\" FROM public.contact_kinds WHERE id=@id")
                .WithParameter("id", input.KindId).GetSingleOrDefaultAsync<string>(token);
            var format = kind switch { "phone" => ContactFormat.Phone, "email" => ContactFormat.Email, _ => (ContactFormat)(-1) };
            var normalized = ClientContactComparison.Normalize(format, input.Value);
            if (normalized is null) continue;
            foreach (var row in rows.Where(row => row.Format == kind))
            {
                var raw = protection.UnprotectValue(new(row.ClientId), new(row.Id), row.Value);
                if (string.Equals(normalized, ClientContactComparison.Normalize(format, raw), StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private async Task<Result<CommandReplay?, ErrorList>> ReadCommandAsync(DatabaseTransaction tx, Guid commandId,
        StaffId authorId, ClientId clientId, string operation, byte[] fingerprint, CancellationToken token)
    {
        var row = await tx.Query("""
            SELECT author_staff_id AS "AuthorId", client_id AS "ClientId", operation AS "Operation",
                protected_request_fingerprint AS "Fingerprint", has_duplicate_contacts AS "HasDuplicateContacts"
            FROM public.client_card_commands WHERE command_id=@id
            """).WithParameter("id", commandId).GetSingleOrDefaultAsync<CommandRow>(token);
        if (row is null) return Result.Success<CommandReplay?, ErrorList>(null);
        if (row.AuthorId != authorId.Value || row.ClientId != clientId.Value || row.Operation != operation
            || !protection.MatchesCommandFingerprint(clientId, commandId, row.Fingerprint, fingerprint))
            return ErrorList.Single(new ModelError.InvalidState(), "commandId");
        if (operation == "restore") return Result.Success<CommandReplay?, ErrorList>(new(null, false));
        var card = await ReadCardAsync(tx, clientId, token);
        return card.IsFailure ? card.Error : Result.Success<CommandReplay?, ErrorList>(new(card.Value, row.HasDuplicateContacts));
    }

    private async Task SaveCommandAsync(DatabaseTransaction tx, Guid commandId, StaffId authorId, ClientId clientId,
        string operation, byte[] fingerprint, bool hasDuplicateContacts, DateTimeOffset now, CancellationToken token) =>
        await tx.Query("""
            INSERT INTO public.client_card_commands(command_id,author_staff_id,client_id,operation,protected_request_fingerprint,has_duplicate_contacts,applied_at)
            VALUES (@command,@author,@client,@operation,@fingerprint,@duplicates,@at)
            """).WithParameter("command", commandId).WithParameter("author", authorId.Value).WithParameter("client", clientId.Value)
            .WithParameter("operation", operation).WithParameter("fingerprint", protection.ProtectCommandFingerprint(clientId, commandId, fingerprint))
            .WithParameter("duplicates", hasDuplicateContacts)
            .WithParameter("at", now).ExecuteAsync(token);

    private static Task LockAsync(DatabaseTransaction tx, string key, CancellationToken token) =>
        tx.Query("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))").WithParameter("key", key).ExecuteAsync(token);

    private static ClientCardValues ToValues(ClientCard card) => new(card.FirstName, card.LastName, card.MiddleName, card.Birthdate,
        card.SourceId, card.Contacts.Select(contact => new ClientContactInput(contact.Id, contact.KindId, contact.Value, contact.Comment, contact.IsPrimary)).ToArray());
    private static bool SameValues(ClientCardValues first, ClientCardValues second) =>
        JsonSerializer.Serialize(first with { Contacts = first.Contacts.OrderBy(item => item.Id).ToArray() }) ==
        JsonSerializer.Serialize(second with { Contacts = second.Contacts.OrderBy(item => item.Id).ToArray() });

    private sealed record CardRow(Guid Id, string FirstName, string LastName, string? MiddleName, string? Birthdate,
        Guid? SourceId, string? SourceName, bool IsArchived, DateTime CreatedAt);
    private sealed record ContactRow(Guid Id, Guid KindId, string KindName, string Format, string Value, string? Comment, bool IsPrimary);
    private sealed record KindRow(string Name, string Format, bool IsArchived);
    private sealed record DuplicateRow(Guid ClientId, string Format, string Value, Guid Id);
    private sealed record CommandRow(Guid AuthorId, Guid ClientId, string Operation, string Fingerprint, bool HasDuplicateContacts);
    private sealed record CommandReplay(ClientCard? Client, bool HasDuplicateContacts);
}
