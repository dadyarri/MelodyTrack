using System.Data;
using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Users.Endpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class PersonalDataEncryptionTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task UserContactFields_AreStoredEncrypted_AndReturnedDecrypted()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        admin.Phone = "+79991234567";
        admin.Telegram = "https://t.me/admin";
        admin.Vk = "https://vk.com/admin";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rawPhone = await ReadScalarAsync("SELECT \"Phone\" FROM public.\"Users\" WHERE \"Id\" = @id", admin.Id.ToByteArray());
        var rawTelegram = await ReadScalarAsync("SELECT \"Telegram\" FROM public.\"Users\" WHERE \"Id\" = @id", admin.Id.ToByteArray());
        var rawVk = await ReadScalarAsync("SELECT \"Vk\" FROM public.\"Users\" WHERE \"Id\" = @id", admin.Id.ToByteArray());

        rawPhone.ShouldNotBeNull();
        rawTelegram.ShouldNotBeNull();
        rawVk.ShouldNotBeNull();
        rawPhone.ShouldNotBe(admin.Phone);
        rawTelegram.ShouldNotBe(admin.Telegram);
        rawVk.ShouldNotBe(admin.Vk);
        rawPhone.ShouldStartWith("enc:v1:");
        rawTelegram.ShouldStartWith("enc:v1:");
        rawVk.ShouldStartWith("enc:v1:");

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (response, payload) = await App.Client.GETAsync<GetUsersEndpoint, EmptyRequest, GetUsersResponse>(EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        var returnedAdmin = payload.Users.Single(user => user.Id == admin.Id);
        returnedAdmin.Phone.ShouldBe(admin.Phone);
        returnedAdmin.Telegram.ShouldBe(admin.Telegram);
        returnedAdmin.Vk.ShouldBe(admin.Vk);
    }

    [Fact]
    public async Task PersonalDataBackfill_EncryptsExistingClientContacts()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var backfill = scope.ServiceProvider.GetRequiredService<IPersonalDataBackfillService>();

        var contactsId = Ulid.NewUlid();
        var clientId = Ulid.NewUlid();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO public."ClientContacts" ("Id", "Phone", "Telegram", "Vk")
            VALUES ({0}, {1}, {2}, {3})
            """,
            contactsId.ToByteArray(),
            "+79990001122",
            "https://t.me/client",
            "https://vk.com/client");

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO public."Clients" ("Id", "FirstName", "LastName", "Patronymic", "DateOfBirth", "SourceId", "CreatedAtUtc", "ContactsId")
            VALUES ({0}, {1}, {2}, NULL, NULL, NULL, {3}, {4})
            """,
            clientId.ToByteArray(),
            "Anna",
            "Petrova",
            DateTime.UtcNow,
            contactsId.ToByteArray());

        await backfill.BackfillAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var rawPhone = await ReadScalarAsync("SELECT \"Phone\" FROM public.\"ClientContacts\" WHERE \"Id\" = @id", contactsId.ToByteArray());
        var rawTelegram = await ReadScalarAsync("SELECT \"Telegram\" FROM public.\"ClientContacts\" WHERE \"Id\" = @id", contactsId.ToByteArray());
        var rawVk = await ReadScalarAsync("SELECT \"Vk\" FROM public.\"ClientContacts\" WHERE \"Id\" = @id", contactsId.ToByteArray());

        rawPhone.ShouldStartWith("enc:v1:");
        rawTelegram.ShouldStartWith("enc:v1:");
        rawVk.ShouldStartWith("enc:v1:");

        var client = await db.Clients
            .Include(item => item.Contacts)
            .FirstAsync(item => item.Id == clientId, TestContext.Current.CancellationToken);

        client.Contacts.Phone.ShouldBe("+79990001122");
        client.Contacts.Telegram.ShouldBe("https://t.me/client");
        client.Contacts.Vk.ShouldBe("https://vk.com/client");
    }

    private async Task<string?> ReadScalarAsync(string sql, byte[] idBytes)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = idBytes;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return result as string;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
