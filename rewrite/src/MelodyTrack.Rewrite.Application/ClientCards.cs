using System.Security.Cryptography;
using System.Text.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed record ClientCardContact(Guid Id, Guid KindId, string KindName, string Format,
    string Value, string? Comment, bool IsPrimary)
{
    public override string ToString() => nameof(ClientCardContact);
}

public sealed record ClientCard(Guid Id, string FirstName, string LastName, string? MiddleName,
    DateOnly? Birthdate, Guid? SourceId, string? SourceName, bool IsArchived, DateTimeOffset CreatedAt,
    IReadOnlyList<ClientCardContact> Contacts)
{
    public override string ToString() => nameof(ClientCard);
}

public sealed record ClientContactInput(Guid Id, Guid KindId, string Value, string? Comment, bool IsPrimary)
{
    public override string ToString() => nameof(ClientContactInput);
}

public sealed record ClientCardValues(string FirstName, string LastName, string? MiddleName,
    DateOnly? Birthdate, Guid? SourceId, IReadOnlyList<ClientContactInput> Contacts)
{
    public override string ToString() => nameof(ClientCardValues);
}

public sealed record ClientCardExpectedState(ClientCardValues Values)
{
    public override string ToString() => nameof(ClientCardExpectedState);
}

public sealed record ClientCardCommandResult(ClientCard? Client, bool HasDuplicateContacts)
{
    public override string ToString() => nameof(ClientCardCommandResult);
}

public interface IClientCardsStore
{
    Task<Result<ClientCard, ErrorList>> ReadAsync(ClientId clientId, CancellationToken cancellationToken);
    Task<Result<ClientCardCommandResult, ErrorList>> CreateAsync(Guid commandId, StaffId authorId, ClientId clientId,
        ClientCardValues values, byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Result<ClientCardCommandResult, ErrorList>> UpdateAsync(Guid commandId, StaffId authorId, ClientId clientId,
        ClientCardExpectedState expectedState, ClientCardValues values, byte[] requestFingerprint,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<UnitResult<ErrorList>> RestoreAsync(Guid commandId, StaffId authorId, ClientId clientId,
        byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class ClientCardsService(IClientCardsStore store)
{
    public async Task<Result<ClientCard, OperationError>> ReadAsync(ClientId clientId, Actor actor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clientId.Value == Guid.Empty) return new OperationError.Validation(ErrorList.Single(new IdentifierError.Empty(), "clientId"));
        if (!CanRead(actor)) return new OperationError.Forbidden();
        var result = await store.ReadAsync(clientId, cancellationToken);
        return result.IsSuccess ? result.Value : ToOperationError(result.Error);
    }

    public async Task<Result<ClientCardCommandResult, OperationError>> CreateAsync(Guid commandId, StaffId authorId,
        ClientId clientId, ClientCardValues values, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ValidateIdentity(commandId, authorId, clientId, values, actor, now);
        if (validation.IsFailure) return validation.Error;
        var valid = ValidateValues(values);
        if (valid.IsFailure) return new OperationError.Validation(valid.Error);
        return Map(await store.CreateAsync(commandId, authorId, clientId, values,
            Fingerprint("create", commandId, clientId, values, null), now, cancellationToken));
    }

    public async Task<Result<ClientCardCommandResult, OperationError>> UpdateAsync(Guid commandId, StaffId authorId,
        ClientId clientId, ClientCardExpectedState? expectedState, ClientCardValues values, Actor actor,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ValidateIdentity(commandId, authorId, clientId, values, actor, now);
        if (validation.IsFailure) return validation.Error;
        if (expectedState is null) return new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "expectedState"));
        var valid = ValidateValues(values);
        if (valid.IsFailure) return new OperationError.Validation(valid.Error);
        return Map(await store.UpdateAsync(commandId, authorId, clientId, expectedState, values,
            Fingerprint("update", commandId, clientId, values, expectedState), now, cancellationToken));
    }

    public async Task<UnitResult<OperationError>> RestoreAsync(Guid commandId, StaffId authorId,
        ClientId clientId, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanWrite(actor, now)) return UnitResult.Failure<OperationError>(new OperationError.Forbidden());
        if (actor is not Actor.Staff staff || staff.Id != authorId)
            return UnitResult.Failure<OperationError>(new OperationError.Forbidden());
        if (commandId == Guid.Empty || authorId.Value == Guid.Empty || clientId.Value == Guid.Empty)
            return UnitResult.Failure<OperationError>(new OperationError.Validation(ErrorList.Single(new IdentifierError.Empty())));
        var hash = Fingerprint("restore", commandId, clientId, null, null);
        var result = await store.RestoreAsync(commandId, authorId, clientId, hash, now, cancellationToken);
        return result.IsSuccess ? UnitResult.Success<OperationError>() : UnitResult.Failure<OperationError>(ToOperationError(result.Error));
    }

    public UnitResult<OperationError> Archive(Actor actor, DateTimeOffset now)
    {
        if (!CanWrite(actor, now)) return UnitResult.Failure<OperationError>(new OperationError.Forbidden());
        return UnitResult.Failure<OperationError>(new OperationError.Conflict(
            ErrorList.Single(new ClientError.ArchiveDependenciesNotMigrated(), "archive")));
    }

    private static UnitResult<OperationError> ValidateIdentity(Guid commandId, StaffId authorId,
        ClientId clientId, ClientCardValues values, Actor actor, DateTimeOffset now)
    {
        if (!CanWrite(actor, now)) return UnitResult.Failure<OperationError>(new OperationError.Forbidden());
        if (commandId == Guid.Empty || authorId.Value == Guid.Empty || clientId.Value == Guid.Empty)
            return UnitResult.Failure<OperationError>(new OperationError.Validation(ErrorList.Single(new IdentifierError.Empty())));
        if (actor is not Actor.Staff staff || staff.Id != authorId)
            return UnitResult.Failure<OperationError>(new OperationError.Forbidden());
        return UnitResult.Success<OperationError>();
    }

    private static UnitResult<ErrorList> ValidateValues(ClientCardValues values)
    {
        var name = PersonName.Create(values.FirstName, values.LastName, values.MiddleName);
        var errors = new ErrorListBuilder().Add(ValidationShim.Identifier(values.SourceId, "sourceId"))
            .Add(ValidationShim.Check(name));
        if (values.Contacts is null) errors.Add(new ModelError.InvalidValue(), "contacts");
        else
        {
            foreach (var contact in values.Contacts)
            {
                if (contact is null) { errors.Add(new ModelError.InvalidValue(), "contacts"); continue; }
                if (contact.Id == Guid.Empty || contact.KindId == Guid.Empty)
                    errors.Add(new IdentifierError.Empty(), "contacts");
            }
            if (values.Contacts.Select(contact => contact?.Id).Distinct().Count() != values.Contacts.Count)
                errors.Add(new ClientError.DuplicateContact(), "contacts");
        }
        return errors.ToResult();
    }

    private static bool CanRead(Actor actor) => actor is Actor.Staff { Permissions: { } permissions }
        && actor.Validate().IsSuccess && permissions.For(StaffPermission.ClientsRead) is
            { Allowed: true, DataScope: PermissionDataScope.Available, TimeScope: PermissionTimeScope.Any };

    private static bool CanWrite(Actor actor, DateTimeOffset now) => actor is Actor.Staff { Permissions: { } permissions }
        && actor.Validate().IsSuccess && actor.HasPermission(StaffPermission.ClientsWrite, recordAt: now, now: now)
        && permissions.For(StaffPermission.ClientsWrite).DataScope == PermissionDataScope.Available;

    private static byte[] Fingerprint(string operation, Guid commandId, ClientId clientId,
        ClientCardValues? values, ClientCardExpectedState? expected) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { operation, commandId, clientId = clientId.Value, values, expected }));

    private static Result<ClientCardCommandResult, OperationError> Map(Result<ClientCardCommandResult, ErrorList> result) =>
        result.IsSuccess ? result.Value : ToOperationError(result.Error);

    private static OperationError ToOperationError(ErrorList errors) => errors.Issues.Any(issue => issue.Error is ModelError.Forbidden)
        ? new OperationError.Forbidden()
        : errors.Issues.Any(issue => issue.Error is ModelError.NotFound)
            ? new OperationError.NotFound()
            : errors.Issues.Any(issue => issue.Error is ModelError.InvalidState or ModelError.Duplicate)
                ? new OperationError.Conflict(errors) : new OperationError.Validation(errors);
}

// Domain validation helpers are internal to Domain; this adapter keeps the application boundary explicit.
internal static class ValidationShim
{
    internal static UnitResult<ErrorList> Identifier(Guid? value, string field) => value is null || value == Guid.Empty
        ? UnitResult.Failure(ErrorList.Single(new ModelError.InvalidValue(), field))
        : UnitResult.Success<ErrorList>();

    internal static UnitResult<ErrorList> Check<T>(Result<T, ErrorList> value) => value.IsFailure
        ? UnitResult.Failure(value.Error) : UnitResult.Success<ErrorList>();
}
