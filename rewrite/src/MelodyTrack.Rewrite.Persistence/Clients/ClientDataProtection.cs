using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Security;

namespace MelodyTrack.Rewrite.Persistence.Clients;

/// <summary>Задаёт устойчивые контексты защиты клиентских заметок и контактов.</summary>
/// <param name="protector">Защита данных нового хранилища.</param>
public sealed class ClientDataProtection(PersonalDataProtector protector)
{
    /// <summary>Защищает заметки с привязкой к клиенту.</summary>
    public string? ProtectNotes(ClientId clientId, string? notes) => protector.Protect(notes, NotesContext(clientId));

    /// <summary>Читает заметки только в контексте исходного клиента.</summary>
    public string? UnprotectNotes(ClientId clientId, string? notes) => protector.Unprotect(notes, NotesContext(clientId));

    /// <summary>Защищает значение контакта с привязкой к клиенту, контакту и полю.</summary>
    public string ProtectValue(ClientId clientId, ClientContactId contactId, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return protector.Protect(value, ContactContext(clientId, contactId) + "/value");
    }

    /// <summary>Читает значение только в контексте исходного контакта клиента.</summary>
    public string UnprotectValue(ClientId clientId, ClientContactId contactId, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return protector.Unprotect(value, ContactContext(clientId, contactId) + "/value");
    }

    /// <summary>Защищает комментарий независимо от значения контакта.</summary>
    public string? ProtectComment(ClientId clientId, ClientContactId contactId, string? comment) =>
        protector.Protect(comment, ContactContext(clientId, contactId) + "/comment");

    /// <summary>Читает комментарий только в контексте исходного контакта клиента.</summary>
    public string? UnprotectComment(ClientId clientId, ClientContactId contactId, string? comment) =>
        protector.Unprotect(comment, ContactContext(clientId, contactId) + "/comment");

    private static string NotesContext(ClientId clientId)
    {
        if (clientId.Value == Guid.Empty) throw new ArgumentException("Требуется непустой ID клиента.", nameof(clientId));
        return $"clients/{clientId.Value:N}/notes";
    }

    private static string ContactContext(ClientId clientId, ClientContactId contactId)
    {
        if (clientId.Value == Guid.Empty) throw new ArgumentException("Требуется непустой ID клиента.", nameof(clientId));
        if (contactId.Value == Guid.Empty) throw new ArgumentException("Требуется непустой ID контакта.", nameof(contactId));
        return $"clients/{clientId.Value:N}/contacts/{contactId.Value:N}";
    }
}
