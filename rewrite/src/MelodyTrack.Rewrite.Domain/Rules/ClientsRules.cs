using System.Collections.Immutable;
using System.Net.Mail;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class ClientSourceRules
{
    public static Result<ClientSource, ErrorList> Create(ClientSourceId id, string? name)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new ClientSource(id, validName.Value);
    }
}

public static class ContactKindRules
{
    public static Result<ContactKind, ErrorList> Create(ContactKindId id, string? name, ContactFormat format)
    {
        var validName = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validName));
        if (validation.IsFailure) return validation.Error;
        return new ContactKind(id, validName.Value, format);
    }

    public static ContactKind Archive(this ContactKind model) => model with { IsArchived = true };

    public static bool CanDelete(bool hasBeenUsed) => !hasBeenUsed;

    public static Result<ContactKind, ErrorList> Rename(this ContactKind model, string? name)
    {
        var validName = Validation.Required("name", name);
        if (validName.IsFailure) return validName.Error;
        return model with { Name = validName.Value };
    }

    public static Result<ContactKind, ErrorList> ChangeFormat(this ContactKind model, bool hasBeenUsed, ContactFormat format)
    {
        if (hasBeenUsed && format != model.Format) return ErrorList.Single(new ClientError.UsedContactFormat());
        return model with { Format = format };
    }
}

public static class ClientContactRules
{
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
            kind.IsArchived ? UnitResult.Failure(ErrorList.Single(new ClientError.ContactKindArchived(), "kindId")) : UnitResult.Success<ErrorList>(),
            valid ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.Single(new ClientError.InvalidContactValue(), "value")));
        if (validation.IsFailure) return validation.Error;
        return new ClientContact(id, kind.Id, value, Validation.OptionalText(comment), isPrimary);
    }

    internal static ClientContact SetPrimary(this ClientContact model, bool primary) => model with { IsPrimary = primary };
}

public static class ClientRules
{
    private static UnitResult<ErrorList> ValidateContacts(ImmutableArray<ClientContact> contacts)
    {
        if (contacts.IsEmpty) return UnitResult.Failure(ErrorList.Single(new ClientError.ContactsRequired(), "contacts"));
        var errors = new ErrorListBuilder();
        if (contacts.Select(contact => contact.Id).Distinct().Count() != contacts.Length)
            errors.Add(new ClientError.DuplicateContact(), "contacts");
        foreach (var group in contacts.GroupBy(contact => contact.KindId))
        {
            if (group.Count(contact => contact.IsPrimary) != 1)
                errors.Add(new ClientError.PrimaryContactRequired(group.Key), "contacts");
        }
        return errors.ToResult();
    }

    public static Result<Client, ErrorList> Create(ClientId id, ClientProfile profile, IEnumerable<ClientContact> contacts)
    {
        var values = contacts.ToImmutableArray();
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(profile.SourceId.Value, "sourceId"), ValidateContacts(values));
        if (validation.IsFailure) return validation.Error;
        return new Client(id, profile, values);
    }

    public static Result<Client, ErrorList> UpdateProfile(this Client model, ClientProfile profile)
    {
        var validation = Validation.Identifier(profile.SourceId.Value, "sourceId");
        if (validation.IsFailure) return validation.Error;
        return model with { Profile = profile };
    }

    public static Client Unarchive(this Client model) => model with { IsArchived = false };

    public static Result<Client, ErrorList> Archive(this Client model, SchedulingUsage usage)
    {
        var validation = Validation.Archive(usage);
        if (validation.IsFailure) return validation.Error;
        return model with { IsArchived = true };
    }

    public static Result<Client, ErrorList> ReplaceContacts(this Client model, IEnumerable<ClientContact> contacts)
    {
        var values = contacts.ToImmutableArray();
        var validation = ValidateContacts(values);
        if (validation.IsFailure) return validation.Error;
        return model with { Contacts = values };
    }

    public static Result<Client, ErrorList> SelectPrimary(this Client model, ClientContactId contactId)
    {
        var selected = ImmutableArrayExtensions.FirstOrDefault(model.Contacts, contact => contact.Id == contactId);
        if (selected is null) return ErrorList.Single(new ClientError.UnknownContact());
        if (selected.IsPrimary) return model;

        var contacts = ImmutableArrayExtensions.Select(model.Contacts, contact => contact.KindId == selected.KindId
            ? contact.SetPrimary(contact.Id == contactId) : contact);
        return model.ReplaceContacts(contacts);
    }

    public static Result<Client, ErrorList> RemoveContact(this Client model, ClientContactId contactId, ClientContactId? replacementId)
    {
        var selected = ImmutableArrayExtensions.FirstOrDefault(model.Contacts, contact => contact.Id == contactId);
        if (selected is null) return ErrorList.Single(new ClientError.UnknownContact());

        var remaining = ImmutableArrayExtensions.Where(model.Contacts, contact => contact.Id != contactId).ToImmutableArray();
        var sameKind = ImmutableArrayExtensions.Where(remaining, contact => contact.KindId == selected.KindId).ToArray();
        if (!selected.IsPrimary || sameKind.Length == 0) return model.ReplaceContacts(remaining);

        var replacement = sameKind.FirstOrDefault(contact => contact.Id == replacementId);
        if (replacement is null) return ErrorList.Single(new ClientError.PrimaryContactRequired(selected.KindId), "contacts");

        var updated = ImmutableArrayExtensions.Select(remaining, contact => contact.KindId == selected.KindId
            ? contact.SetPrimary(contact.Id == replacement.Id) : contact);
        return model.ReplaceContacts(updated);
    }
}
