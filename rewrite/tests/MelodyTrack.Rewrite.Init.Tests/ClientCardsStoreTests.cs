using System.Security.Cryptography;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Security;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientCardsStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId Author = new(Guid.Parse("019a2c63-8c00-7000-8000-000000009001"));
    private static readonly Guid Source = Guid.Parse("019a2c63-8c00-7000-8000-000000009002");
    private static readonly Guid FirstPhoneKind = Guid.Parse("019a2c63-8c00-7000-8000-000000009003");
    private static readonly Guid SecondPhoneKind = Guid.Parse("019a2c63-8c00-7000-8000-000000009004");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task CreateAsync_CrossKindDuplicateWarnsAndReplayDoesNotWriteAgain() => WithStore(async (store, database, protection) =>
    {
        var existingId = new ClientId(Guid.Parse("019a2c63-8c00-7000-8000-000000009010"));
        await database.Query("""
            INSERT INTO public.clients(id,first_name,last_name,source_id,created_at) VALUES (@id,'Existing','Client',@source,@at);
            INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,is_primary)
            VALUES (@contact,@id,@kind,@value,true)
            """).WithParameter("id", existingId.Value).WithParameter("source", Source).WithParameter("at", Now)
            .WithParameter("contact", Guid.Parse("019a2c63-8c00-7000-8000-000000009011"))
            .WithParameter("kind", FirstPhoneKind)
            .WithParameter("value", protection.ProtectValue(existingId, new(Guid.Parse("019a2c63-8c00-7000-8000-000000009011")), "+7 (912) 345-67-89"))
            .ExecuteAsync(Token);
        var clientId = new ClientId(Guid.Parse("019a2c63-8c00-7000-8000-000000009012"));
        var values = new ClientCardValues("New", "Client", null, null, Source,
            [new(Guid.Parse("019a2c63-8c00-7000-8000-000000009013"), SecondPhoneKind, "8 912 345 67 89", null, true)]);
        var fingerprint = SHA256.HashData("synthetic-request"u8);
        var commandId = Guid.Parse("019a2c63-8c00-7000-8000-000000009014");

        var created = await store.CreateAsync(commandId, Author, clientId, values, fingerprint, Now, Token);

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.HasDuplicateContacts);
        Assert.Equal(clientId.Value, created.Value.Client!.Id);
        Assert.Equal(2, await database.Query("SELECT count(*) FROM public.clients").GetSingleAsync<long>(Token));
        await database.Query("DELETE FROM public.clients WHERE id=@id").WithParameter("id", existingId.Value).ExecuteAsync(Token);
        var replayed = await store.CreateAsync(commandId, Author, clientId, values, fingerprint, Now, Token);

        Assert.True(replayed.IsSuccess);
        Assert.True(replayed.Value.HasDuplicateContacts);
        Assert.Equal(1, await database.Query("SELECT count(*) FROM public.clients").GetSingleAsync<long>(Token));
        var storedFingerprint = await database.Query("SELECT protected_request_fingerprint FROM public.client_card_commands WHERE command_id=@id")
            .WithParameter("id", commandId).GetSingleAsync<string>(Token);
        Assert.DoesNotContain(Convert.ToHexString(fingerprint), storedFingerprint, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public Task RestoreAsync_ArchivedImportedCard_LeavesRevokedAuthenticationHistoryRevoked() => WithStore(async (store, database, _) =>
    {
        var clientId = new ClientId(Guid.Parse("019a2c63-8c00-7000-8000-000000009020"));
        var linkHash = new string('A', 64);
        var sessionId = Guid.Parse("019a2c63-8c00-7000-8000-000000009022");
        await database.Query("INSERT INTO public.clients(id,first_name,last_name,created_at) VALUES (@id,'Imported','Client',@at)")
            .WithParameter("id", clientId.Value).WithParameter("at", Now).ExecuteAsync(Token);
        await database.Query("INSERT INTO public.client_access_links(link_hash,client_id,issued_by,issued_at) VALUES (@hash,@client,@author,@at)")
            .WithParameter("hash", linkHash).WithParameter("client", clientId.Value).WithParameter("author", Author.Value).WithParameter("at", Now).ExecuteAsync(Token);
        await database.Query("INSERT INTO public.client_access_sessions(id,link_hash,refresh_hash,created_at) VALUES (@session,@hash,@refresh,@at)")
            .WithParameter("session", sessionId).WithParameter("hash", linkHash).WithParameter("refresh", new string('B', 64)).WithParameter("at", Now).ExecuteAsync(Token);
        await database.Query("UPDATE public.clients SET is_archived=true WHERE id=@client").WithParameter("client", clientId.Value).ExecuteAsync(Token);
        var command = Guid.Parse("019a2c63-8c00-7000-8000-000000009021");

        var restored = await store.RestoreAsync(command, Author, clientId, SHA256.HashData("restore"u8), Now, Token);

        Assert.True(restored.IsSuccess);
        Assert.False(await database.Query("SELECT is_archived FROM public.clients WHERE id=@id").WithParameter("id", clientId.Value).GetSingleAsync<bool>(Token));
        Assert.True(await database.Query("SELECT revoked_at IS NOT NULL FROM public.client_access_links WHERE link_hash=@hash")
            .WithParameter("hash", linkHash).GetSingleAsync<bool>(Token));
        Assert.True(await database.Query("SELECT revoked_at IS NOT NULL FROM public.client_access_sessions WHERE id=@id")
            .WithParameter("id", sessionId).GetSingleAsync<bool>(Token));
        Assert.Equal("restore", await database.Query("SELECT operation FROM public.client_card_commands WHERE command_id=@id").WithParameter("id", command).GetSingleAsync<string>(Token));
    });

    [Fact]
    public Task UpdateAsync_UnchangedLegacyContact_PreservesRawTextAndAllowsCommentEdit() => WithStore(async (store, database, protection) =>
    {
        var clientId = new ClientId(Guid.Parse("019a2c63-8c00-7000-8000-000000009030"));
        var contactId = Guid.Parse("019a2c63-8c00-7000-8000-000000009031");
        await database.Query("INSERT INTO public.clients(id,first_name,last_name,source_id,created_at) VALUES (@id,'Old','Client',@source,@at)")
            .WithParameter("id", clientId.Value).WithParameter("source", Source).WithParameter("at", Now).ExecuteAsync(Token);
        await database.Query("UPDATE public.contact_kinds SET is_archived=true WHERE id=@id").WithParameter("id", FirstPhoneKind).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
            VALUES (@contact,@client,@kind,@value,@comment,true)
            """).WithParameter("contact", contactId).WithParameter("client", clientId.Value).WithParameter("kind", FirstPhoneKind)
            .WithParameter("value", protection.ProtectValue(clientId, new(contactId), " historical invalid "))
            .WithParameter("comment", protection.ProtectComment(clientId, new(contactId), " original ")).ExecuteAsync(Token);
        var original = new ClientCardValues("Old", "Client", null, null, Source,
            [new(contactId, FirstPhoneKind, " historical invalid ", " original ", true)]);
        var changed = original with { FirstName = "Updated", Contacts = [new(contactId, FirstPhoneKind, " historical invalid ", " updated ", true)] };

        var result = await store.UpdateAsync(Guid.Parse("019a2c63-8c00-7000-8000-000000009032"), Author, clientId,
            new(original), changed, SHA256.HashData("update-raw"u8), Now, Token);
        var saved = await store.ReadAsync(clientId, Token);

        Assert.True(result.IsSuccess);
        Assert.True(saved.IsSuccess);
        Assert.Equal(" historical invalid ", saved.Value.Contacts.Single().Value);
        Assert.Equal(" updated ", saved.Value.Contacts.Single().Comment);
    });

    [Fact]
    public Task UpdateAsync_StaleExpectedState_LeavesCardUnchanged() => WithStore(async (store, database, _) =>
    {
        var clientId = new ClientId(Guid.Parse("019a2c63-8c00-7000-8000-000000009040"));
        await database.Query("INSERT INTO public.clients(id,first_name,last_name,source_id,created_at) VALUES (@id,'Current','Client',@source,@at)")
            .WithParameter("id", clientId.Value).WithParameter("source", Source).WithParameter("at", Now).ExecuteAsync(Token);
        var expected = new ClientCardValues("Stale", "Client", null, null, Source, []);
        var attempted = expected with { FirstName = "Overwritten" };

        var result = await store.UpdateAsync(Guid.Parse("019a2c63-8c00-7000-8000-000000009041"), Author, clientId,
            new(expected), attempted, SHA256.HashData("stale-update"u8), Now, Token);
        var saved = await store.ReadAsync(clientId, Token);

        Assert.True(result.IsFailure);
        Assert.True(saved.IsSuccess);
        Assert.Equal("Current", saved.Value.FirstName);
    });

    private async Task WithStore(Func<ClientCardsStore, Database, ClientDataProtection, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql", "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql", "0012_CreateClientAccessSessions.sql", "0016_CreateClientCardCommands.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token);
            await database.Query(sql).ExecuteAsync(Token);
        }
        using var protector = new PersonalDataProtector("test", new Dictionary<string, string>
        {
            ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var protection = new ClientDataProtection(protector);
        await database.Query("INSERT INTO public.staff_access_state(staff_id,role) VALUES (@id,'superuser')")
            .WithParameter("id", Author.Value).ExecuteAsync(Token);
        await database.Query("INSERT INTO public.client_sources(id,name) VALUES (@id,'Reference')")
            .WithParameter("id", Source).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.contact_kinds(id,name,format) VALUES (@first,'Phone','phone'),(@second,'Mobile','phone')
            """).WithParameter("first", FirstPhoneKind).WithParameter("second", SecondPhoneKind).ExecuteAsync(Token);
        await scenario(new ClientCardsStore(database, protection), database, protection);
    }
}
