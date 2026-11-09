using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

// Text belongs to protected subject history, never to the general audit or technical log.
public sealed record ClientNotesChange
{
    private ClientNotesChange(Guid id, ClientId clientId, StaffId authorId, DateTimeOffset changedAt,
        string? before, string? after)
    {
        Id = id; ClientId = clientId; AuthorId = authorId; ChangedAt = changedAt;
        Before = before; After = after;
    }

    public Guid Id { get; }
    public ClientId ClientId { get; }
    public StaffId AuthorId { get; }
    public DateTimeOffset ChangedAt { get; }
    public string? Before { get; }
    public string? After { get; }

    public static Result<ClientNotesChange, ErrorList> Create(Guid id, ClientId clientId, StaffId authorId,
        DateTimeOffset changedAt, string? before, string? after)
    {
        var validation = Validation.Collect(Validation.Identifier(id), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(authorId.Value, "authorId"));
        if (validation.IsFailure) return validation.Error;
        if (string.Equals(before, after, StringComparison.Ordinal))
            return ErrorList.Single(new ModelError.InvalidState(), "notes");
        // PostgreSQL timestamptz persists microseconds; canonical time also keeps command replay stable.
        var utc = new DateTimeOffset(changedAt.UtcTicks - changedAt.UtcTicks % 10, TimeSpan.Zero);
        return new ClientNotesChange(id, clientId, authorId, utc, before, after);
    }

    public override string ToString() => nameof(ClientNotesChange);
}
