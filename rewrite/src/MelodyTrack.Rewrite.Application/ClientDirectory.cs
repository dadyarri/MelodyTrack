using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed record ClientDirectoryQuery(string? Q = null, string Archive = "active", Guid? SourceId = null,
    bool WithoutSource = false, string Sort = "name:asc", int Page = 1, int PageSize = 10)
{
    public override string ToString() => nameof(ClientDirectoryQuery);

    public UnitResult<ErrorList> Validate()
    {
        var errors = new ErrorListBuilder();
        void Invalid(bool invalid, string field)
        {
            if (invalid) errors.Add(new ModelError.InvalidValue(), field);
        }
        Invalid(Q?.Length > 200, "q");
        Invalid(Archive is not ("active" or "archived" or "all"), "archive");
        Invalid(SourceId == Guid.Empty || SourceId is not null && WithoutSource, "sourceId");
        Invalid(ReadSorts().IsFailure, "sort");
        Invalid(Page < 1, "page");
        Invalid(PageSize is < 1 or > 100, "pageSize");
        return errors.ToResult();
    }

    public Result<IReadOnlyList<ClientDirectorySort>, ErrorList> ReadSorts()
    {
        if (string.IsNullOrWhiteSpace(Sort))
            return Result.Success<IReadOnlyList<ClientDirectorySort>, ErrorList>([]);
        var parts = Sort.Split(',');
        if (parts.Length > 5) return ErrorList.Single(new ModelError.InvalidValue(), "sort");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var sorts = new List<ClientDirectorySort>();
        foreach (var part in parts)
        {
            var pair = part.Split(':');
            if (pair.Length != 2 || pair[0] is not ("name" or "status" or "createdAt" or "birthdate" or "source")
                || pair[1] is not ("asc" or "desc") || !keys.Add(pair[0]))
                return ErrorList.Single(new ModelError.InvalidValue(), "sort");
            sorts.Add(new(pair[0], pair[1] == "desc"));
        }
        return sorts;
    }
}

public sealed record ClientDirectorySort(string Key, bool Descending);

public sealed record ClientDirectoryContact(Guid Id, Guid KindId, string KindName, string Format, string Value,
    string? Comment, bool IsPrimary)
{
    public override string ToString() => nameof(ClientDirectoryContact);
}

public sealed record ClientDirectoryEntry(Guid Id, string FirstName, string LastName, string? MiddleName,
    DateOnly? Birthdate, Guid? SourceId, string? SourceName, bool IsArchived, DateTimeOffset CreatedAt,
    IReadOnlyList<ClientDirectoryContact> Contacts)
{
    public override string ToString() => nameof(ClientDirectoryEntry);
}

public sealed record ClientDirectoryPage(IReadOnlyList<ClientDirectoryEntry> Items, int Page, int PageSize,
    int TotalCount, int TotalPages)
{
    public override string ToString() => nameof(ClientDirectoryPage);
}

public sealed record ClientDirectorySource(Guid Id, string Name);
public sealed record ClientDirectoryContactKind(Guid Id, string Name, string Format, bool IsArchived);
public sealed record ClientDirectoryOptions(IReadOnlyList<ClientDirectorySource> Sources,
    IReadOnlyList<ClientDirectoryContactKind> ContactKinds);

public interface IClientDirectoryStore
{
    Task<ClientDirectoryPage> ReadAsync(ClientDirectoryQuery query, CancellationToken cancellationToken);
    Task<ClientDirectoryOptions> ReadOptionsAsync(CancellationToken cancellationToken);
}

public sealed class ClientDirectoryService(IClientDirectoryStore store)
{
    public async Task<Result<ClientDirectoryPage, OperationError>> ReadAsync(ClientDirectoryQuery query,
        Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(actor)) return new OperationError.Forbidden();
        var valid = query.Validate();
        if (valid.IsFailure) return new OperationError.Validation(valid.Error);
        return await store.ReadAsync(query with { Q = query.Q?.Trim() }, cancellationToken);
    }

    public async Task<Result<ClientDirectoryOptions, OperationError>> ReadOptionsAsync(Actor actor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(actor)) return new OperationError.Forbidden();
        return await store.ReadOptionsAsync(cancellationToken);
    }

    // Own clients require scheduling relations that are not present in this storage batch.
    // A limited grant must never widen to all clients or masquerade as an empty result.
    private static bool CanRead(Actor actor) => actor is Actor.Staff { Permissions: { } permissions }
        && actor.Validate().IsSuccess && permissions.For(StaffPermission.ClientsRead) is
            { Allowed: true, DataScope: PermissionDataScope.Available, TimeScope: PermissionTimeScope.Any };
}
