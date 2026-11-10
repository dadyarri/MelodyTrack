using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientAuthenticationStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly ClientId Client = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly StaffId Staff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private static Actor Administrator => new Actor.Staff(Staff, StaffRole.Administrator);

    [Fact]
    public Task SignOutAsync_OneOfTwoDevices_RevokesOnlyCurrentSessionAndPreservesLink() => WithStore(async (authentication, _, _) =>
    {
        var link = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var first = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));
        var second = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));
        var current = Assert.IsType<ClientSessionIdentity>(await authentication.AuthenticateAsync(first.AccessToken, Token));
        Assert.Equal(Client, current.ClientId);

        await authentication.SignOutAsync(current, Token);

        Assert.Null(await authentication.AuthenticateAsync(first.AccessToken, Token));
        Assert.Null(await authentication.RefreshAsync(first.RefreshToken, Token));
        Assert.NotNull(await authentication.AuthenticateAsync(second.AccessToken, Token));
        Assert.NotNull(await authentication.RefreshAsync(second.RefreshToken, Token));
        Assert.NotNull(await authentication.SignInAsync(link.Secret, Token));
    });

    [Fact]
    public Task RefreshAsync_OldRememberedSessionAndExpiredAccess_IssuesNewAccessWithoutPeriodicSessionExpiry() => WithStore(async (authentication, _, database) =>
    {
        var link = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var session = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));
        await database.Query("""
            UPDATE public.client_access_sessions SET created_at=clock_timestamp()-INTERVAL '90 days';
            UPDATE public.client_access_tokens SET issued_at=CURRENT_TIMESTAMP-INTERVAL '6 minutes', expires_at=CURRENT_TIMESTAMP-INTERVAL '1 minute';
            """).ExecuteAsync(Token);
        Assert.Null(await authentication.AuthenticateAsync(session.AccessToken, Token));

        var refreshed = Assert.IsType<ClientSessionCredentials>(await authentication.RefreshAsync(session.RefreshToken, Token));

        Assert.Equal(Client, Assert.IsType<ClientSessionIdentity>(await authentication.AuthenticateAsync(refreshed.AccessToken, Token)).ClientId);
        Assert.Equal(300, refreshed.ExpiresIn);
        Assert.Equal(300m, await database.Query("SELECT EXTRACT(EPOCH FROM (expires_at-issued_at)) FROM public.client_access_tokens WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(refreshed.AccessToken)).GetSingleAsync<decimal>(Token));
    });

    [Fact]
    public Task ReplaceLinkAsync_ExistingDevices_RevokesAllPreviousLinkSessionsPermanently() => WithStore(async (authentication, _, _) =>
    {
        var oldLink = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var first = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(oldLink.Secret, Token));
        var second = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(oldLink.Secret, Token));

        var replacement = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));

        Assert.Null(await authentication.SignInAsync(oldLink.Secret, Token));
        Assert.Null(await authentication.RefreshAsync(first.RefreshToken, Token));
        Assert.Null(await authentication.RefreshAsync(second.RefreshToken, Token));
        Assert.Null(await authentication.AuthenticateAsync(first.AccessToken, Token));
        Assert.Null(await authentication.AuthenticateAsync(second.AccessToken, Token));
        Assert.NotNull(await authentication.SignInAsync(replacement.Secret, Token));
    });

    [Fact]
    public Task RevokeLinkAsync_ExistingDevice_RequiresNewLinkForLaterLogin() => WithStore(async (authentication, _, _) =>
    {
        var link = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var session = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));

        Assert.True((await authentication.RevokeLinkAsync(Client, Guid.NewGuid(), Administrator, Token)).IsSuccess);

        Assert.Null(await authentication.SignInAsync(link.Secret, Token));
        Assert.Null(await authentication.RefreshAsync(session.RefreshToken, Token));
        Assert.Null(await authentication.AuthenticateAsync(session.AccessToken, Token));
    });

    [Fact]
    public Task ReplaceLinkAsync_ArchivedThenRestoredClient_DoesNotReviveOldCredentials() => WithStore(async (authentication, _, database) =>
    {
        var link = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var session = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));
        await database.Query("UPDATE public.clients SET is_archived=true WHERE id=@id").WithParameter("id", Client.Value).ExecuteAsync(Token);
        Assert.True((await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token)).IsFailure);
        await database.Query("UPDATE public.clients SET is_archived=false WHERE id=@id").WithParameter("id", Client.Value).ExecuteAsync(Token);

        Assert.Null(await authentication.SignInAsync(link.Secret, Token));
        Assert.Null(await authentication.RefreshAsync(session.RefreshToken, Token));
        Assert.Null(await authentication.AuthenticateAsync(session.AccessToken, Token));
        var replacement = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        Assert.NotNull(await authentication.SignInAsync(replacement.Secret, Token));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ReplaceLinkAsync_IssuerAccessChanged_DeniesStaleApplicationSnapshot(bool blocked) => WithStore(async (authentication, _, database) =>
    {
        var update = blocked ? "UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id"
            : "UPDATE public.staff_access_state SET role='teacher' WHERE staff_id=@id";
        await database.Query(update).WithParameter("id", Staff.Value).ExecuteAsync(Token);

        var result = await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.client_access_links").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ReplaceLinkAsync_ReplayAfterNewerLink_PreservesNewAccessWithoutReturningSecret() => WithStore(async (authentication, _, database) =>
    {
        var command = Guid.NewGuid();
        var original = Ok(await authentication.ReplaceLinkAsync(Client, command, Administrator, Token));
        var replacement = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var current = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(replacement.Secret, Token));

        var replay = await authentication.ReplaceLinkAsync(Client, command, Administrator, Token);

        Assert.True(replay.IsSuccess);
        Assert.Null(replay.Value);
        Assert.Null(await authentication.SignInAsync(original.Secret, Token));
        Assert.NotNull(await authentication.AuthenticateAsync(current.AccessToken, Token));
        Assert.NotNull(await authentication.SignInAsync(replacement.Secret, Token));
        Assert.Equal(2, await database.Query("SELECT count(*)::integer FROM public.client_access_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task RevokeLinkAsync_ReplayAfterNewerLink_DoesNotRevokeReplacement() => WithStore(async (authentication, _, database) =>
    {
        Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var command = Guid.NewGuid();
        Assert.True((await authentication.RevokeLinkAsync(Client, command, Administrator, Token)).IsSuccess);
        var replacement = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var current = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(replacement.Secret, Token));

        var replay = await authentication.RevokeLinkAsync(Client, command, Administrator, Token);

        Assert.True(replay.IsSuccess);
        Assert.NotNull(await authentication.AuthenticateAsync(current.AccessToken, Token));
        Assert.NotNull(await authentication.SignInAsync(replacement.Secret, Token));
        Assert.Equal(3, await database.Query("SELECT count(*)::integer FROM public.client_access_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ReplaceLinkAsync_ConcurrentIdenticalCommands_IssuesOnlyOneSecretAndReceipt() => WithStore(async (authentication, _, database) =>
    {
        var command = Guid.NewGuid();

        var results = await Task.WhenAll(authentication.ReplaceLinkAsync(Client, command, Administrator, Token),
            authentication.ReplaceLinkAsync(Client, command, Administrator, Token));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        var issued = Assert.IsType<ClientLinkSecret>(Assert.Single(results, result => result.Value is not null).Value);
        Assert.Single(results, result => result.Value is null);
        Assert.NotNull(await authentication.SignInAsync(issued.Secret, Token));
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.client_access_links").GetSingleAsync<int>(Token));
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.client_access_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task RevokeLinkAsync_CommandUsedForReplacement_RejectsChangedMeaning() => WithStore(async (authentication, _, _) =>
    {
        var command = Guid.NewGuid();
        var issued = Ok(await authentication.ReplaceLinkAsync(Client, command, Administrator, Token));

        var result = await authentication.RevokeLinkAsync(Client, command, Administrator, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.InvalidState && issue.Field == "commandId");
        Assert.NotNull(await authentication.SignInAsync(issued.Secret, Token));
    });

    [Fact]
    public Task ReplaceLinkAsync_ReplayAfterIssuerDemotion_DeniesBeforeReadingReceipt() => WithStore(async (authentication, _, database) =>
    {
        var command = Guid.NewGuid();
        Ok(await authentication.ReplaceLinkAsync(Client, command, Administrator, Token));
        await database.Query("UPDATE public.staff_access_state SET role='teacher' WHERE staff_id=@id")
            .WithParameter("id", Staff.Value).ExecuteAsync(Token);

        var result = await authentication.ReplaceLinkAsync(Client, command, Administrator, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.client_access_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task RefreshAsync_RacesLinkRotation_CannotLeaveAnOldSessionTokenUsable() => WithStore(async (authentication, _, _) =>
    {
        var link = Ok(await authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token));
        var session = Assert.IsType<ClientSessionCredentials>(await authentication.SignInAsync(link.Secret, Token));
        var refresh = authentication.RefreshAsync(session.RefreshToken, Token);
        var replace = authentication.ReplaceLinkAsync(Client, Guid.NewGuid(), Administrator, Token);

        await Task.WhenAll(refresh, replace);

        Assert.True((await replace).IsSuccess);
        var renewed = await refresh;
        if (renewed is not null) Assert.Null(await authentication.AuthenticateAsync(renewed.AccessToken, Token));
        Assert.Null(await authentication.RefreshAsync(session.RefreshToken, Token));
    });

    private async Task WithStore(Func<ClientAuthentication, ClientAuthenticationStore, Database, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql",
            "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql", "0012_CreateClientAccessSessions.sql", "0013_CreateBrowserAccessContexts.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.staff_access_state(staff_id,role) VALUES (@staff,'administrator');
            INSERT INTO public.clients(id,first_name,last_name,created_at) VALUES (@client,'Synthetic','Client',CURRENT_TIMESTAMP);
            """).WithParameter("staff", Staff.Value).WithParameter("client", Client.Value).ExecuteAsync(Token);
        var store = new ClientAuthenticationStore(database, new TrustedInternalBrowserAccessContext());
        await scenario(new(store), store, database);
    }

    private static ClientLinkSecret Ok(Result<ClientLinkSecret?, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return Assert.IsType<ClientLinkSecret>(result.Value);
    }
}
