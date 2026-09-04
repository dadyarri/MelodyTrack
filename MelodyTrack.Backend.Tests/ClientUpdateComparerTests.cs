using MelodyTrack.Backend.Api.Clients;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Data.Models;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ClientUpdateComparerTests
{
    public static TheoryData<string, Action<UpdateClientRequest>> ChangedFields => new()
    {
        { "first name", request => request.FirstName = "Changed" },
        { "last name", request => request.LastName = "Changed" },
        { "patronymic", request => request.Patronymic = "Changed" },
        { "date of birth", request => request.DateOfBirth = new DateOnly(2001, 2, 3) },
        { "email", request => request.Email = "changed@example.com" },
        { "phone", request => request.Phone = "+79990000001" },
        { "telegram", request => request.Telegram = "@changed" },
        { "vk", request => request.Vk = "https://vk.com/changed" },
        { "source", request => request.SourceId = Ulid.NewUlid() },
        { "vacations", request => request.Vacations = [new ClientVacationRequest { StartDate = Utc(2026, 9, 1), EndDate = Utc(2026, 9, 3) }] }
    };

    [Fact]
    public void IsNoOp_EquivalentNormalizedAndReorderedValues_ReturnsTrue()
    {
        var client = CreateClient();
        client.Vacations =
        [
            CreateVacation(client, Utc(2026, 8, 10), Utc(2026, 8, 13)),
            CreateVacation(client, Utc(2026, 7, 1), Utc(2026, 7, 4))
        ];
        var request = CreateMatchingRequest(client);
        request.Email = "  CLIENT@EXAMPLE.COM ";
        request.Vacations =
        [
            new ClientVacationRequest { StartDate = Utc(2026, 7, 1), EndDate = Utc(2026, 7, 4) },
            new ClientVacationRequest { StartDate = Utc(2026, 8, 10), EndDate = Utc(2026, 8, 13) }
        ];

        ClientUpdateComparer.IsNoOp(client, request).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void IsNoOp_EachChangedField_ReturnsFalse(string _, Action<UpdateClientRequest> change)
    {
        var client = CreateClient();
        var request = CreateMatchingRequest(client);
        change(request);

        ClientUpdateComparer.IsNoOp(client, request).ShouldBeFalse();
    }

    [Fact]
    public void IsNoOp_OmittedPatchNamesAndVacations_LeavesThoseFieldsUnchanged()
    {
        var client = CreateClient();
        var request = CreateMatchingRequest(client);
        request.FirstName = null;
        request.LastName = null;
        request.Vacations = null;

        ClientUpdateComparer.IsNoOp(client, request).ShouldBeTrue();
    }

    private static Client CreateClient() => new()
    {
        Id = Ulid.NewUlid(), FirstName = "Anna", LastName = "Client", Patronymic = "Petrovna",
        DateOfBirth = new DateOnly(2000, 1, 2), SourceId = Ulid.NewUlid(), CreatedAtUtc = DateTime.UtcNow,
        Contacts = new ClientContacts
        {
            Id = Ulid.NewUlid(), Email = "client@example.com", Phone = "+79990000000",
            Telegram = "@client", Vk = "https://vk.com/client"
        }
    };

    private static UpdateClientRequest CreateMatchingRequest(Client client) => new()
    {
        Id = client.Id, FirstName = client.FirstName, LastName = client.LastName, Patronymic = client.Patronymic,
        DateOfBirth = client.DateOfBirth, Email = client.Contacts.Email, Phone = client.Contacts.Phone,
        Telegram = client.Contacts.Telegram, Vk = client.Contacts.Vk, SourceId = client.SourceId
    };

    private static ClientVacation CreateVacation(Client client, DateTime start, DateTime end) => new()
    {
        Id = Ulid.NewUlid(), Client = client, ClientId = client.Id, StartDate = start, EndDate = end
    };

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
