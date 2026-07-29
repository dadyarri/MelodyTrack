using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;

namespace MelodyTrack.Backend.Api.Clients;

internal static class ClientUpdateComparer
{
    public static bool IsNoOp(Client client, UpdateClientRequest request)
    {
        var changes = new[]
        {
            IsFirstNameChanged(client, request),
            IsLastNameChanged(client, request),
            IsPatronymicChanged(client, request),
            IsDateOfBirthChanged(client, request),
            IsEmailChanged(client, request),
            IsPhoneChanged(client, request),
            IsTelegramChanged(client, request),
            IsVkChanged(client, request),
            IsSourceChanged(client, request),
            AreVacationsChanged(client, request)
        };

        return !changes.Contains(true);
    }

    internal static bool IsFirstNameChanged(Client client, UpdateClientRequest request) =>
        request.FirstName is not null && request.FirstName != client.FirstName;

    internal static bool IsLastNameChanged(Client client, UpdateClientRequest request) =>
        request.LastName is not null && request.LastName != client.LastName;

    internal static bool IsPatronymicChanged(Client client, UpdateClientRequest request) =>
        request.Patronymic != client.Patronymic;

    internal static bool IsDateOfBirthChanged(Client client, UpdateClientRequest request) =>
        request.DateOfBirth != client.DateOfBirth;

    internal static bool IsEmailChanged(Client client, UpdateClientRequest request) =>
        NormalizeEmail(request.Email) != client.Contacts.Email;

    internal static bool IsPhoneChanged(Client client, UpdateClientRequest request) =>
        request.Phone != client.Contacts.Phone;

    internal static bool IsTelegramChanged(Client client, UpdateClientRequest request) =>
        request.Telegram != client.Contacts.Telegram;

    internal static bool IsVkChanged(Client client, UpdateClientRequest request) =>
        request.Vk != client.Contacts.Vk;

    internal static bool IsSourceChanged(Client client, UpdateClientRequest request) =>
        request.SourceId != client.SourceId;

    internal static bool AreVacationsChanged(Client client, UpdateClientRequest request)
    {
        if (request.Vacations is null)
        {
            return false;
        }

        var current = client.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => (item.StartDate, item.EndDate));
        var requested = request.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => (item.StartDate, item.EndDate));
        return !current.SequenceEqual(requested);
    }

    internal static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : UserUtils.NormalizeEmail(email);
}
