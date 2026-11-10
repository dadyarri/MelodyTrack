using System.Collections.Immutable;
using System.Net.Mail;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record PersonName
{
    private PersonName(string first, string last, string? middle) { First = first; Last = last; Middle = middle; }
    public string First { get; }
    public string Last { get; }
    public string? Middle { get; }

    public static Result<PersonName, ErrorList> Create(string? first, string? last, string? middle)
    {
        var validFirst = Validation.Required("firstName", first);
        var validLast = Validation.Required("lastName", last);
        var validation = Validation.Collect(Validation.Check(validFirst), Validation.Check(validLast));
        if (validation.IsFailure) return validation.Error;
        return new PersonName(validFirst.Value, validLast.Value, Validation.OptionalText(middle));
    }
}

public sealed record ClientSource
{
    internal ClientSource(ClientSourceId id, string name) { Id = id; Name = name; }

    public ClientSourceId Id { get; }

    public string Name { get; }
}

public enum ContactFormat { Phone, Email, Link }

public sealed record ContactKind
{
    internal ContactKind(ContactKindId id, string name, ContactFormat format) { Id = id; Name = name; Format = format; }

    public ContactKindId Id { get; }

    public string Name { get; internal init; }

    public ContactFormat Format { get; internal init; }

    public bool IsArchived { get; internal init; }
}

public sealed record ClientContact
{
    internal ClientContact(ClientContactId id, ContactKindId kindId, string value, string? comment, bool primary)
    {
        Id = id; KindId = kindId; Value = value; Comment = comment; IsPrimary = primary;
    }

    public ClientContactId Id { get; }

    public ContactKindId KindId { get; }

    public string Value { get; }

    public string? Comment { get; }

    public bool IsPrimary { get; internal init; }
}

public sealed record ClientProfile(PersonName Name, DateOnly? Birthdate, ClientSourceId? SourceId, string? Notes);

public sealed record Client
{
    internal Client(ClientId id, ClientProfile profile, ImmutableArray<ClientContact> contacts) { Id = id; Profile = profile; Contacts = contacts; }

    public ClientId Id { get; }

    public DateTimeOffset CreatedAt { get; internal init; }

    public ClientProfile Profile { get; internal init; }

    public ImmutableArray<ClientContact> Contacts { get; internal init; }

    public bool IsArchived { get; internal init; }
}
