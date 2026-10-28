using System.Collections.Immutable;
using System.Net.Mail;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Scheduling;

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
    private ClientSource(ClientSourceId id, string name) { Id = id; Name = name; }
    public ClientSourceId Id { get; }
    public string Name { get; }

    public static Result<ClientSource, ErrorList> Create(ClientSourceId id, string? name)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new ClientSource(id, validName.Value);
    }
}

public enum ContactFormat { Phone, Email, Link }

public sealed record ContactKind
{
    private ContactKind(ContactKindId id, string name, ContactFormat format) { Id = id; Name = name; Format = format; }
    public ContactKindId Id { get; }
    public string Name { get; private init; }
    public ContactFormat Format { get; private init; }
    public bool IsArchived { get; private init; }

    public static Result<ContactKind, ErrorList> Create(ContactKindId id, string? name, ContactFormat format)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new ContactKind(id, validName.Value, format);
    }

    public ContactKind Archive() => this with { IsArchived = true };
    public static bool CanDelete(bool hasBeenUsed) => !hasBeenUsed;

    public Result<ContactKind, ErrorList> Rename(string? name)
    {
        var validName = Validation.Required("name", name);
        if (validName.IsFailure) return validName.Error;
        return this with { Name = validName.Value };
    }

    public Result<ContactKind, ErrorList> ChangeFormat(bool hasBeenUsed, ContactFormat format)
    {
        if (hasBeenUsed && format != Format) return ErrorList.Of(new ClientError.UsedContactFormat());
        return this with { Format = format };
    }
}

public sealed record ClientContact
{
    private ClientContact(ClientContactId id, ContactKindId kindId, string value, string? comment, bool primary)
    {
        Id = id; KindId = kindId; Value = value; Comment = comment; IsPrimary = primary;
    }
    public ClientContactId Id { get; }
    public ContactKindId KindId { get; }
    public string Value { get; }
    public string? Comment { get; }
    public bool IsPrimary { get; private init; }

    public static Result<ClientContact, ErrorList> Create(ClientContactId id, ContactKind kind, string? value, string? comment, bool isPrimary)
    {
        value = value?.Trim() ?? "";
        var valid = kind.Format switch
        {
            ContactFormat.Phone => !string.IsNullOrWhiteSpace(value) && value.Any(char.IsDigit)
                && value.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || "+()-.".Contains(character))
                && value.Count(character => character == '+') <= 1 && (!value.Contains('+') || value.StartsWith('+')),
            ContactFormat.Email => MailAddress.TryCreate(value, out _),
            ContactFormat.Link => Uri.IsWellFormedUriString(value, UriKind.Absolute),
            _ => false
        };
        var validation = Validation.Collect(Validation.Identifier(id.Value),
            kind.IsArchived ? UnitResult.Failure(ErrorList.Of(new ClientError.ContactKindArchived(), "kindId")) : UnitResult.Success<ErrorList>(),
            valid ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.Of(new ClientError.InvalidContactValue(), "value")));
        if (validation.IsFailure) return validation.Error;
        return new ClientContact(id, kind.Id, value, Validation.OptionalText(comment), isPrimary);
    }

    internal ClientContact SetPrimary(bool primary) => this with { IsPrimary = primary };
}

public sealed record ClientProfile(PersonName Name, DateOnly? Birthday, ClientSourceId SourceId, string? Notes);

public sealed record Client
{
    private Client(ClientId id, ClientProfile profile, ImmutableArray<ClientContact> contacts) { Id = id; Profile = profile; Contacts = contacts; }
    public ClientId Id { get; }
    public ClientProfile Profile { get; private init; }
    public ImmutableArray<ClientContact> Contacts { get; private init; }
    public bool IsArchived { get; private init; }

    private static UnitResult<ErrorList> ValidateContacts(ImmutableArray<ClientContact> contacts)
    {
        if (contacts.IsEmpty) return UnitResult.Failure(ErrorList.Of(new ClientError.ContactsRequired(), "contacts"));
        var errors = new List<DomainIssue>();
        if (contacts.Select(contact => contact.Id).Distinct().Count() != contacts.Length)
            errors.Add(new(new ClientError.DuplicateContact(), "contacts"));
        foreach (var group in contacts.GroupBy(contact => contact.KindId))
        {
            if (group.Count(contact => contact.IsPrimary) != 1)
                errors.Add(new(new ClientError.PrimaryContactRequired(group.Key), "contacts"));
        }
        return errors.Count == 0 ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.From(errors));
    }

    public static Result<Client, ErrorList> Create(ClientId id, ClientProfile profile, IEnumerable<ClientContact> contacts)
    {
        var values = contacts.ToImmutableArray();
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(profile.SourceId.Value, "sourceId"), ValidateContacts(values));
        if (validation.IsFailure) return validation.Error;
        return new Client(id, profile, values);
    }

    public Result<Client, ErrorList> UpdateProfile(ClientProfile profile)
    {
        var validation = Validation.Identifier(profile.SourceId.Value, "sourceId");
        if (validation.IsFailure) return validation.Error;
        return this with { Profile = profile };
    }

    public Client Unarchive() => this with { IsArchived = false };

    public Result<Client, ErrorList> Archive(SchedulingUsage usage)
    {
        var validation = Validation.Archive(usage);
        if (validation.IsFailure) return validation.Error;
        return this with { IsArchived = true };
    }

    public Result<Client, ErrorList> ReplaceContacts(IEnumerable<ClientContact> contacts)
    {
        var values = contacts.ToImmutableArray();
        var validation = ValidateContacts(values);
        if (validation.IsFailure) return validation.Error;
        return this with { Contacts = values };
    }

    public Result<Client, ErrorList> SelectPrimary(ClientContactId contactId)
    {
        var selected = Contacts.FirstOrDefault(contact => contact.Id == contactId);
        if (selected is null) return ErrorList.Of(new ClientError.UnknownContact());
        if (selected.IsPrimary) return this;

        var contacts = Contacts.Select(contact => contact.KindId == selected.KindId
            ? contact.SetPrimary(contact.Id == contactId) : contact);
        return ReplaceContacts(contacts);
    }

    public Result<Client, ErrorList> RemoveContact(ClientContactId contactId, ClientContactId? replacementId)
    {
        var selected = Contacts.FirstOrDefault(contact => contact.Id == contactId);
        if (selected is null) return ErrorList.Of(new ClientError.UnknownContact());

        var remaining = Contacts.Where(contact => contact.Id != contactId).ToImmutableArray();
        var sameKind = remaining.Where(contact => contact.KindId == selected.KindId).ToArray();
        if (!selected.IsPrimary || sameKind.Length == 0) return ReplaceContacts(remaining);

        var replacement = sameKind.FirstOrDefault(contact => contact.Id == replacementId);
        if (replacement is null) return ErrorList.Of(new ClientError.PrimaryContactRequired(selected.KindId), "contacts");

        var updated = remaining.Select(contact => contact.KindId == selected.KindId
            ? contact.SetPrimary(contact.Id == replacement.Id) : contact);
        return ReplaceContacts(updated);
    }
}
