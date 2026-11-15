using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Api.Tests;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OtpNet;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class FullAuthenticationEndpointsTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Password = "Synthetic passphrase 27!";

    [Fact]
    public async Task Restore_CurrentBrowserCookie_RotatesSessionWithoutJsonCredentials()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "cookie-restored-teacher");

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);
        var restored = (await response.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotEqual(signedIn.AccessToken, restored.AccessToken);
        Assert.NotEqual(signedIn.RefreshToken, restored.RefreshToken);
        AssertRefreshCookie(response, restored);
        using var identity = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, restored.AccessToken);
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        using var again = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        AssertRefreshCookie(again, (await again.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://foreign.example.test")]
    public async Task Restore_UntrustedOrigin_RejectsWithoutConsumingCookieSession(string? origin)
    {
        await using var host = await HostAsync();
        await RegisterTeacherAsync(host, "cookie-origin-teacher");

        using var denied = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: origin);
        using var restored = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        AssertNoRefreshCookie(denied);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RevokedOrExpiredSession_ReturnsUnauthorized(bool expired)
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "cookie-rejected-teacher");
        var sql = expired
            ? "UPDATE public.staff_sessions SET refresh_expires_at=clock_timestamp()-INTERVAL '1 second' WHERE refresh_hash=@hash"
            : "UPDATE public.staff_sessions SET revoked_at=clock_timestamp() WHERE refresh_hash=@hash";
        await host.Database.Query(sql).WithParameter("hash", OpaqueTokens.Hash(signedIn.RefreshToken!)).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertRefreshCookieCleared(response);
        Assert.DoesNotContain(signedIn.RefreshToken!, await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Restore_InactiveBrowserSession_DoesNotReactivateRememberedAccount()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "cookie-inactive-teacher");
        await host.Database.Query("""
            UPDATE public.browser_access_contexts SET active_kind=NULL,active_session_id=NULL
            WHERE active_session_id=(SELECT id FROM public.staff_sessions WHERE refresh_hash=@hash)
            """).WithParameter("hash", OpaqueTokens.Hash(signedIn.RefreshToken!)).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertRefreshCookieCleared(response);
        Assert.True(await host.Database.Query("""
            SELECT active_session_id IS NULL FROM public.browser_access_contexts
            WHERE id=(SELECT browser_context_id FROM public.staff_sessions WHERE refresh_hash=@hash)
            """).WithParameter("hash", OpaqueTokens.Hash(signedIn.RefreshToken!)).GetSingleAsync<bool>(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ProtectedRole_RequiresPreviouslyVerifiedFactor(bool verified)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "cookie-protected-administrator", authenticator: true);
        var signedIn = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge, authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!)
        });
        await host.Database.Query("UPDATE public.staff_sessions SET factor_verified=@verified WHERE refresh_hash=@hash")
            .WithParameter("verified", verified).WithParameter("hash", OpaqueTokens.Hash(signedIn.RefreshToken!)).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);

        Assert.Equal(verified ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (verified) AssertRefreshCookie(response, (await response.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!);
        else AssertRefreshCookieCleared(response);
    }

    [Fact]
    public async Task Logout_CookieSession_ClearsBrowserRestoreAccess()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "cookie-logout-teacher");

        using var logout = await host.SendAsync(HttpMethod.Post, "/api/auth/logout", null, signedIn.AccessToken);
        using var restore = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/restore", null, origin: host.Origin);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cookie = Assert.Single(logout.Headers.GetValues("Set-Cookie")
            .Select(value => Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(value)),
            value => value.Name.ToString() == StaffSessionCookies.Name);
        Assert.Equal(DateTimeOffset.UnixEpoch, cookie.Expires);
        Assert.Equal(HttpStatusCode.Unauthorized, restore.StatusCode);
    }

    [Theory]
    [InlineData("teacher")]
    [InlineData("administrator")]
    [InlineData("superuser")]
    public async Task PreviewInvitation_ActiveInvitation_ReturnsRoleWithoutConsumingOrCreatingCredentials(string role)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, role);
        var credentialCount = await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials").GetSingleAsync<int>(Token);
        var challengeCount = await host.Database.Query("SELECT count(*)::integer FROM public.staff_credential_challenges").GetSingleAsync<int>(Token);

        using var first = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/invitation", new { link = invitation });
        using var second = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/invitation", new { link = invitation });

        foreach (var response in new[] { first, second })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var preview = await response.Content.ReadFromJsonAsync<StaffRegistrationInvitationResponse>(Token);
            Assert.Equal(role, preview!.Role);
        }
        Assert.False(await host.Database.Query("SELECT consumed FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).GetSingleAsync<bool>(Token));
        Assert.Equal(credentialCount, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials").GetSingleAsync<int>(Token));
        Assert.Equal(challengeCount, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credential_challenges").GetSingleAsync<int>(Token));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    public async Task PreviewInvitation_UnusableInvitation_ReturnsUniformUnauthorized(string state)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "teacher");
        if (state == "expired")
            await host.Database.Query("UPDATE public.staff_credential_links SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        else if (state == "consumed")
            await host.Database.Query("UPDATE public.staff_credential_links SET consumed=true WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        else invitation = state == "unknown" ? OpaqueTokens.Create() : "invalid";

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/invitation", new { link = invitation });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("Требуется вход", problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("role", out _));
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("superuser")]
    public async Task AdvanceRegistrationAuthenticator_ConfirmedCode_RotatesBoundedProofWithoutCreatingAccount(string role)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, role);
        var registration = await BeginRegistrationAsync(host, invitation, "staged-authenticator", authenticator: true);
        await host.Database.Query("UPDATE public.staff_credential_challenges SET expires_at=clock_timestamp()+INTERVAL '2 minutes' WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(registration.Challenge!)).ExecuteAsync(Token);
        var originalExpiry = await host.Database.Query("SELECT expires_at FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(registration.Challenge!)).GetSingleAsync<DateTime>(Token);

        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new
        {
            challenge = registration.Challenge, code = await CodeAsync(host, registration.AuthenticatorSecret!), skip = false
        });

        Assert.NotNull(advanced.Challenge);
        Assert.NotEqual(registration.Challenge, advanced.Challenge);
        Assert.NotNull(advanced.OptionsJson);
        Assert.Null(advanced.AuthenticatorSecret);
        Assert.Null(advanced.AccessToken);
        Assert.Null(advanced.RefreshToken);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='staged-authenticator'")
            .GetSingleAsync<int>(Token));
        Assert.False(await host.Database.Query("SELECT consumed FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).GetSingleAsync<bool>(Token));
        Assert.Equal(originalExpiry, await host.Database.Query("SELECT expires_at FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(advanced.Challenge)).GetSingleAsync<DateTime>(Token));

        var completed = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = advanced.Challenge, authenticatorCode = "not-a-code"
        });

        Assert.NotNull(completed.AccessToken);
        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, completed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("superuser")]
    public async Task AdvanceRegistrationAuthenticator_SkippedWithPassword_RequiresPasskeyAssertionBeforeWorkingAccess(string role)
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        var invitation = await InviteAsync(host, role);
        var registration = await BeginRegistrationAsync(host, invitation, "staged-passkey-factor", authenticator: true);

        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new
        {
            challenge = registration.Challenge, skip = true
        });
        using var attestation = JsonDocument.Parse(authenticator.Register(advanced.OptionsJson!));
        var completed = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = advanced.Challenge, passkeyResponse = attestation.RootElement
        });

        Assert.Null(completed.AccessToken);
        Assert.Null(completed.RefreshToken);
        Assert.Equal("authenticatorOrPasskey", completed.Factor);
        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var assertion = JsonDocument.Parse(authenticator.Assert(completed.OptionsJson!));

        var verified = await PostStaffAsync(host, "/api/auth/staff/factor", new
        {
            challenge = completed.Challenge, passkeyResponse = assertion.RootElement
        });

        Assert.NotNull(verified.AccessToken);
        Assert.True(await host.Database.Query("SELECT protected_authenticator IS NULL AND NOT second_factor_enabled FROM public.staff_credentials WHERE login='staged-passkey-factor'")
            .GetSingleAsync<bool>(Token));
    }

    [Fact]
    public async Task AdvanceRegistrationAuthenticator_TeacherSkipsOptionalAuthenticator_AllowsPasswordOnlyRegistration()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "teacher");
        var registration = await BeginRegistrationAsync(host, invitation, "staged-password-only", authenticator: true);

        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new { challenge = registration.Challenge, skip = true });
        var completed = await PostStaffAsync(host, "/api/auth/staff/register/complete", new { challenge = advanced.Challenge });

        Assert.NotNull(completed.AccessToken);
        Assert.True(await host.Database.Query("SELECT protected_authenticator IS NULL AND NOT second_factor_enabled FROM public.staff_credentials WHERE login='staged-password-only'")
            .GetSingleAsync<bool>(Token));
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("superuser")]
    public async Task CompleteRegistration_MandatoryRoleSkipsBothFactors_DoesNotCreateAccount(string role)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, role);
        var registration = await BeginRegistrationAsync(host, invitation, "staged-no-factor", authenticator: true);
        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new { challenge = registration.Challenge, skip = true });

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/complete", new { challenge = advanced.Challenge });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='staged-no-factor'")
            .GetSingleAsync<int>(Token));
        Assert.False(await host.Database.Query("SELECT consumed FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).GetSingleAsync<bool>(Token));
    }

    [Fact]
    public async Task AdvanceRegistrationAuthenticator_InvitationExpiresBeforeOriginalProof_BoundsNewProofByInvitation()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "staged-short-invitation", authenticator: true);
        await host.Database.Query("UPDATE public.staff_credential_links SET expires_at=clock_timestamp()+INTERVAL '1 minute' WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        var invitationExpiry = await host.Database.Query("SELECT expires_at FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).GetSingleAsync<DateTime>(Token);

        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new
        {
            challenge = registration.Challenge, code = await CodeAsync(host, registration.AuthenticatorSecret!), skip = false
        });

        Assert.Equal(invitationExpiry, await host.Database.Query("SELECT expires_at FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(advanced.Challenge!)).GetSingleAsync<DateTime>(Token));
    }

    [Theory]
    [InlineData("expired-proof")]
    [InlineData("expired-invitation")]
    [InlineData("consumed-invitation")]
    public async Task CompleteRegistration_StagedProofOrInvitationBecomesInvalid_DoesNotBypassValidityChecks(string state)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "staged-invalid-completion", authenticator: true);
        var advanced = await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new
        {
            challenge = registration.Challenge, code = await CodeAsync(host, registration.AuthenticatorSecret!), skip = false
        });
        if (state == "expired-proof")
            await host.Database.Query("UPDATE public.staff_credential_challenges SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(advanced.Challenge!)).ExecuteAsync(Token);
        else if (state == "expired-invitation")
            await host.Database.Query("UPDATE public.staff_credential_links SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        else
            await host.Database.Query("UPDATE public.staff_credential_links SET consumed=true WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/complete", new { challenge = advanced.Challenge });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='staged-invalid-completion'")
            .GetSingleAsync<int>(Token));
    }

    [Theory]
    [InlineData("teacher", HttpStatusCode.OK)]
    [InlineData("administrator", HttpStatusCode.Unauthorized)]
    [InlineData("superuser", HttpStatusCode.Unauthorized)]
    public async Task AdvanceRegistrationAuthenticator_SkippedWithoutPassword_PreservesRoleProtection(string role, HttpStatusCode expected)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, role);
        var registration = await PostStaffAsync(host, "/api/auth/staff/register/options", new
        {
            link = invitation, login = "staged-passwordless", password = (string?)null, authenticator = true, secondFactor = true
        });

        using var advancedResponse = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/authenticator",
            new { challenge = registration.Challenge, skip = true });

        Assert.Equal(expected, advancedResponse.StatusCode);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='staged-passwordless'")
            .GetSingleAsync<int>(Token));
        Assert.False(await host.Database.Query("SELECT consumed FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).GetSingleAsync<bool>(Token));
        if (expected == HttpStatusCode.OK)
        {
            using var authenticator = new WebAuthnTestAuthenticator();
            var advanced = (await advancedResponse.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!;
            using var attestation = JsonDocument.Parse(authenticator.Register(advanced.OptionsJson!));
            var completed = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
            {
                challenge = advanced.Challenge, passkeyResponse = attestation.RootElement
            });
            Assert.NotNull(completed.AccessToken);
        }
    }

    [Theory]
    [InlineData("expired-proof")]
    [InlineData("used-proof")]
    [InlineData("expired-invitation")]
    [InlineData("consumed-invitation")]
    [InlineData("incorrect-code")]
    public async Task AdvanceRegistrationAuthenticator_InvalidProofOrInvitation_DoesNotCreateCredentials(string state)
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "staged-invalid", authenticator: true);
        var code = await CodeAsync(host, registration.AuthenticatorSecret!);
        if (state == "expired-proof")
            await host.Database.Query("UPDATE public.staff_credential_challenges SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(registration.Challenge!)).ExecuteAsync(Token);
        else if (state == "expired-invitation")
            await host.Database.Query("UPDATE public.staff_credential_links SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        else if (state == "consumed-invitation")
            await host.Database.Query("UPDATE public.staff_credential_links SET consumed=true WHERE token_hash=@hash")
                .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);
        else if (state == "used-proof")
            await PostStaffAsync(host, "/api/auth/staff/register/authenticator", new { challenge = registration.Challenge, code, skip = false });
        else code = "not-a-code";

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/authenticator",
            new { challenge = registration.Challenge, code, skip = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='staged-invalid'")
            .GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task Register_TeacherInvitation_CreatesAccountAndPasswordSessionOnce()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "teacher");
        var registration = await BeginRegistrationAsync(host, invitation, "teacher-one");

        var signedIn = await PostStaffAsync(host, "/api/auth/staff/register/complete", new { challenge = registration.Challenge });

        Assert.NotNull(signedIn.AccessToken);
        Assert.NotNull(signedIn.RefreshToken);
        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, signedIn.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync(Token));
        Assert.Equal("teacher-one", document.RootElement.GetProperty("login").GetString());
        Assert.Equal("teacher", document.RootElement.GetProperty("role").GetString());
        var passwordSession = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "TEACHER-ONE", password = Password });
        Assert.NotNull(passwordSession.AccessToken);
        Assert.NotEqual(signedIn.AccessToken, passwordSession.AccessToken);
        using var reused = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/options",
            new { link = invitation, login = "second-account", password = Password, authenticator = false, secondFactor = false });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(1, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='teacher-one'").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task Password_Administrator_RequiresAuthenticatorBeforeWorkingAccess()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "administrator-one", authenticator: true);
        var signup = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge,
            authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!)
        });
        Assert.NotNull(signup.AccessToken);

        var password = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "administrator-one", password = Password });

        Assert.Null(password.AccessToken);
        Assert.Null(password.RefreshToken);
        Assert.Equal("authenticatorOrPasskey", password.Factor);
        Assert.NotNull(password.Challenge);
        var completed = await PostStaffAsync(host, "/api/auth/staff/factor", new
        {
            challenge = password.Challenge,
            code = await CodeAsync(host, registration.AuthenticatorSecret!, futureStep: true)
        });
        Assert.NotNull(completed.AccessToken);
        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, completed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task BeginPasskey_NoRegisteredAccounts_ReturnsAnonymousDiscoverableChallenge()
    {
        await using var host = await HostAsync();
        await host.Database.Query("TRUNCATE TABLE public.staff_access_state CASCADE").ExecuteAsync(Token);

        var response = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);

        Assert.NotNull(response.Challenge);
        Assert.Null(response.AccessToken);
        Assert.Null(response.StaffId);
        using var options = JsonDocument.Parse(response.OptionsJson!);
        Assert.True(!options.RootElement.TryGetProperty("allowCredentials", out var allowed) || allowed.GetArrayLength() == 0);
        Assert.Equal("required", options.RootElement.GetProperty("userVerification").GetString());
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials").GetSingleAsync<int>(Token));
        Assert.True(await host.Database.Query("SELECT staff_id IS NULL FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(response.Challenge!)).GetSingleAsync<bool>(Token));
    }

    [Fact]
    public async Task BeginPasskey_ExpiredAnonymousProofs_RemovesOnlyOwnExpiredProofs()
    {
        await using var host = await HostAsync();
        using var otherBrowser = host.CreateBrowser();
        var expiredOwn = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        var liveOwn = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        var expiredOther = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null, browser: otherBrowser);
        await host.Database.Query("""
            UPDATE public.staff_credential_challenges SET expires_at=clock_timestamp()-INTERVAL '1 second'
            WHERE token_hash=@own OR token_hash=@other
            """).WithParameter("own", OpaqueTokens.Hash(expiredOwn.Challenge!))
                .WithParameter("other", OpaqueTokens.Hash(expiredOther.Challenge!)).ExecuteAsync(Token);

        await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);

        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(expiredOwn.Challenge!)).GetSingleAsync<int>(Token));
        Assert.Equal(1, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(liveOwn.Challenge!)).GetSingleAsync<int>(Token));
        Assert.Equal(1, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credential_challenges WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(expiredOther.Challenge!)).GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task CompletePasskey_DiscoverableCredential_ResolvesSelectedOwnerWithoutLogin()
    {
        await using var host = await HostAsync();
        using var first = new WebAuthnTestAuthenticator();
        using var second = new WebAuthnTestAuthenticator();
        var selected = await RegisterPasskeyTeacherAsync(host, "selected-discoverable", first);
        await RegisterPasskeyTeacherAsync(host, "other-discoverable", second);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(first.Assert(options.OptionsJson!));

        var completed = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", new
        {
            challenge = options.Challenge, response = assertion.RootElement
        });

        Assert.Equal(selected.StaffId, completed.StaffId);
        Assert.NotNull(completed.AccessToken);
        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, completed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var identity = (await me.Content.ReadFromJsonAsync<CurrentStaffResponse>(Token))!;
        Assert.Equal(selected.StaffId, identity.StaffId);
    }

    [Theory]
    [InlineData("unknown-key")]
    [InlineData("missing-handle")]
    [InlineData("null-handle")]
    [InlineData("mismatched-handle")]
    [InlineData("blocked")]
    [InlineData("archived")]
    [InlineData("malformed")]
    [InlineData("wrong-origin")]
    [InlineData("wrong-challenge")]
    public async Task CompletePasskey_UnusableSelectedCredential_ReturnsUnauthorized(string state)
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        var account = await RegisterPasskeyTeacherAsync(host, "rejected-discoverable", authenticator);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        var assertion = JsonNode.Parse(authenticator.Assert(options.OptionsJson!,
            origin: state == "wrong-origin" ? "https://untrusted.invalid" : "https://localhost",
            challengeOverride: state == "wrong-challenge" ? OpaqueTokens.Create() : null))!;
        if (state == "unknown-key")
        {
            var unknown = OpaqueTokens.Create();
            assertion["id"] = unknown;
            assertion["rawId"] = unknown;
        }
        else if (state == "missing-handle") assertion["response"]!.AsObject().Remove("userHandle");
        else if (state == "null-handle") assertion["response"]!["userHandle"] = null;
        else if (state == "mismatched-handle") assertion["response"]!["userHandle"] = "AQID";
        else if (state == "malformed") assertion = new JsonObject();
        else if (state == "blocked")
            await host.Database.Query("UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id")
                .WithParameter("id", account.StaffId!.Value).ExecuteAsync(Token);
        else if (state == "archived")
            await host.Database.Query("UPDATE public.staff_access_state SET is_archived=true WHERE staff_id=@id")
                .WithParameter("id", account.StaffId!.Value).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/passkey/complete", new
        {
            challenge = options.Challenge, response = assertion
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task CompletePasskey_ReusedDiscoveryProof_ReturnsUnauthorized()
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        await RegisterPasskeyTeacherAsync(host, "replayed-discoverable", authenticator);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(authenticator.Assert(options.OptionsJson!));
        var request = new { challenge = options.Challenge, response = assertion.RootElement };
        var first = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", request);

        using var replay = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/passkey/complete", request);

        Assert.NotNull(first.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task CompletePasskey_DifferentBrowser_CannotConsumeDiscoveryProof()
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        using var otherBrowser = host.CreateBrowser();
        await RegisterPasskeyTeacherAsync(host, "browser-bound-discoverable", authenticator);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(authenticator.Assert(options.OptionsJson!));
        var request = new { challenge = options.Challenge, response = assertion.RootElement };

        using var other = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/passkey/complete", request, browser: otherBrowser);
        var original = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", request);

        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        Assert.NotNull(original.AccessToken);
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("superuser")]
    public async Task Passkey_ProtectedRole_RequiresTotpAndRejectsSecondPasskeyAsFactor(string role)
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        var invitation = await InviteAsync(host, role);
        var registration = await BeginRegistrationAsync(host, invitation, "passkey-administrator", authenticator: true);
        using var attestation = JsonDocument.Parse(authenticator.Register(registration.OptionsJson!));
        var signup = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge,
            authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!),
            passkeyResponse = attestation.RootElement
        });
        Assert.NotNull(signup.AccessToken);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(authenticator.Assert(options.OptionsJson!));

        var primary = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", new { challenge = options.Challenge, response = assertion.RootElement });

        Assert.Null(primary.AccessToken);
        Assert.Equal("authenticator", primary.Factor);
        Assert.Null(primary.OptionsJson);
        using var wrongFactor = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/factor",
            new { challenge = primary.Challenge, passkeyResponse = assertion.RootElement });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongFactor.StatusCode);

        var freshOptions = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var freshAssertion = JsonDocument.Parse(authenticator.Assert(freshOptions.OptionsJson!));
        var freshPrimary = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", new { challenge = freshOptions.Challenge, response = freshAssertion.RootElement });
        var completed = await PostStaffAsync(host, "/api/auth/staff/factor", new
        {
            challenge = freshPrimary.Challenge,
            code = await CodeAsync(host, registration.AuthenticatorSecret!, futureStep: true)
        });
        Assert.NotNull(completed.AccessToken);
    }

    [Fact]
    public async Task Refresh_RotatesSecret_RejectsOldRefreshAndPreservesFiveMinuteAccessAfterExpiry()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "refresh-teacher");

        var refreshed = await PostStaffAsync(host, "/api/auth/staff/refresh", new { refreshToken = signedIn.RefreshToken });

        Assert.NotEqual(signedIn.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(signedIn.AccessToken, refreshed.AccessToken);
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = signedIn.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        await host.Database.Query("UPDATE public.staff_sessions SET refresh_expires_at=clock_timestamp()-INTERVAL '1 second' WHERE refresh_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(refreshed.RefreshToken!)).ExecuteAsync(Token);

        using var expiredRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = refreshed.RefreshToken });
        using var remainingAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, refreshed.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, expiredRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, remainingAccess.StatusCode);
    }

    [Fact]
    public async Task GetMe_CurrentCredentials_ReturnsOwnCanonicalLoginWithoutAcceptingAnotherStaffId()
    {
        await using var host = await HostAsync();
        using var otherBrowser = host.CreateBrowser();
        var own = await RegisterTeacherAsync(host, "  MiXeD.Login  ");
        var other = await RegisterTeacherAsync(host, "other-login", otherBrowser);

        using var response = await host.SendAsync(HttpMethod.Get, $"/api/auth/me?staffId={other.StaffId}", null, own.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var identity = (await response.Content.ReadFromJsonAsync<CurrentStaffResponse>(Token))!;
        Assert.Equal(own.StaffId, identity.StaffId);
        Assert.Equal("MiXeD.Login", identity.Login);
    }

    [Fact]
    public async Task GetMe_CredentialRecordRemoved_ReturnsUnauthorizedInsteadOfIncompleteIdentity()
    {
        await using var host = await HostAsync();
        var own = await RegisterTeacherAsync(host, "removed-login");
        await host.Database.Query("DELETE FROM public.staff_credentials WHERE staff_id=@id")
            .WithParameter("id", own.StaffId!.Value).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, own.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("Требуется вход", problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("login", out _));
    }

    [Fact]
    public async Task GetMe_TeacherPromotedWithoutVerifiedFactor_DeniesOldSession()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "promoted-teacher");
        await host.Database.Query("UPDATE public.staff_access_state SET role='administrator' WHERE staff_id=@id")
            .WithParameter("id", signedIn.StaffId!.Value).ExecuteAsync(Token);

        using var me = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, signedIn.AccessToken);
        using var refresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = signedIn.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
    }

    [Fact]
    public async Task Register_ExpiredInvitation_DoesNotCreateCredentials()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "teacher");
        await host.Database.Query("UPDATE public.staff_credential_links SET expires_at=clock_timestamp()-INTERVAL '1 second' WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(invitation)).ExecuteAsync(Token);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/options",
            new { link = invitation, login = "expired-invite", password = Password, authenticator = false, secondFactor = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await host.Database.Query("SELECT count(*)::integer FROM public.staff_credentials WHERE login='expired-invite'").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task Recovery_OneUseLink_RevokesPreviousAccessAndReplacesLostCredentials()
    {
        await using var host = await HostAsync();
        var original = await RegisterTeacherAsync(host, "recovery-teacher");
        using var secondBrowser = host.CreateBrowser();
        var second = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "recovery-teacher", password = Password }, browser: secondBrowser);

        var recovery = await PostStaffAsync(host, $"/api/auth/staff/{original.StaffId}/recovery", new { }, host.SuperToken);

        using var firstAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, original.AccessToken);
        using var secondAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, second.AccessToken, secondBrowser);
        using var oldPassword = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/password", new { login = "recovery-teacher", password = Password });
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, firstAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var seconds = await host.Database.Query("SELECT EXTRACT(EPOCH FROM (expires_at-clock_timestamp())) FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(recovery.Challenge!)).GetSingleAsync<decimal>(Token);
        Assert.InRange(seconds, 86340m, 86400m);
        const string replacementPassword = "Replacement synthetic passphrase 28!";
        var setup = await PostStaffAsync(host, "/api/auth/staff/register/options", new
        {
            link = recovery.Challenge, login = "recovery-teacher", password = replacementPassword, authenticator = false, secondFactor = false
        });
        var restored = await PostStaffAsync(host, "/api/auth/staff/register/complete", new { challenge = setup.Challenge });
        Assert.Equal(original.StaffId, restored.StaffId);
        Assert.NotNull(restored.AccessToken);
        using var reusedLink = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/register/options", new
        {
            link = recovery.Challenge, login = "recovery-teacher", password = replacementPassword, authenticator = false, secondFactor = false
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reusedLink.StatusCode);
        Assert.NotNull((await PostStaffAsync(host, "/api/auth/staff/password", new { login = "recovery-teacher", password = replacementPassword })).AccessToken);
    }

    [Fact]
    public async Task ChangePassword_WrongOldPassword_LeavesExistingPasswordUsable()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "password-rejection");

        using var response = await host.SendAsync(HttpMethod.Put, "/api/auth/staff/security/password",
            new { oldPassword = "Wrong synthetic password", newPassword = "New synthetic passphrase 28!" }, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await PostStaffAsync(host, "/api/auth/staff/password", new { login = "password-rejection", password = Password })).AccessToken);
    }

    [Fact]
    public async Task ChangePassword_CorrectOldPassword_ReplacesPasswordVerification()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "password-change");
        const string replacement = "New synthetic passphrase 28!";

        using var response = await host.SendAsync(HttpMethod.Put, "/api/auth/staff/security/password",
            new { oldPassword = Password, newPassword = replacement }, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var previous = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/password", new { login = "password-change", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, previous.StatusCode);
        Assert.NotNull((await PostStaffAsync(host, "/api/auth/staff/password", new { login = "password-change", password = replacement })).AccessToken);
    }

    [Fact]
    public async Task ConfirmAuthenticator_EnrolledSecret_ActivatesFactorOnlyAfterProof()
    {
        await using var host = await HostAsync();
        var signedIn = await RegisterTeacherAsync(host, "enrolled-teacher");
        var enrollment = await PostStaffAsync(host, "/api/auth/staff/security/authenticator/options", new { }, signedIn.AccessToken);
        using var before = await host.SendAsync(HttpMethod.Get, "/api/auth/staff/security", null, signedIn.AccessToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var initialProfile = JsonDocument.Parse(await before.Content.ReadAsStringAsync(Token));
        Assert.False(initialProfile.RootElement.GetProperty("authenticatorEnabled").GetBoolean());

        using var confirmation = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/security/authenticator/confirm", new
        {
            challenge = enrollment.Challenge,
            code = await CodeAsync(host, enrollment.AuthenticatorSecret!)
        }, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var after = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "enrolled-teacher", password = Password });
        Assert.Null(after.AccessToken);
        Assert.Equal("authenticatorOrPasskey", after.Factor);
    }

    [Fact]
    public async Task DisableAuthenticator_AdministratorsOnlyFactor_IsForbidden()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "factor-removal", authenticator: true);
        var signedIn = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge, authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!)
        });

        using var response = await host.SendAsync(HttpMethod.Delete, "/api/auth/staff/security/authenticator",
            new { code = await CodeAsync(host, registration.AuthenticatorSecret!, futureStep: true) }, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var login = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "factor-removal", password = Password });
        Assert.Null(login.AccessToken);
        Assert.Equal("authenticatorOrPasskey", login.Factor);
    }

    [Fact]
    public async Task RemovePasskey_OnlyCredential_IsForbidden()
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        var signedIn = await RegisterPasskeyTeacherAsync(host, "passkey-only", authenticator);

        using var removal = await host.SendAsync(HttpMethod.Delete,
            $"/api/auth/staff/security/passkeys/{authenticator.CredentialId}", null, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, removal.StatusCode);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(authenticator.Assert(options.OptionsJson!));
        Assert.NotNull((await PostStaffAsync(host, "/api/auth/staff/passkey/complete", new { challenge = options.Challenge, response = assertion.RootElement })).AccessToken);
    }

    [Fact]
    public async Task AddPasskey_SecondCredential_CanReplaceOriginalCredential()
    {
        await using var host = await HostAsync();
        using var original = new WebAuthnTestAuthenticator();
        using var replacement = new WebAuthnTestAuthenticator();
        var signedIn = await RegisterPasskeyTeacherAsync(host, "two-passkeys", original);
        var options = await PostStaffAsync(host, "/api/auth/staff/security/passkeys/options", new { }, signedIn.AccessToken);
        using var registration = JsonDocument.Parse(replacement.Register(options.OptionsJson!));

        using var addition = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/security/passkeys/complete",
            new { challenge = options.Challenge, response = registration.RootElement }, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.OK, addition.StatusCode);
        using var removal = await host.SendAsync(HttpMethod.Delete,
            $"/api/auth/staff/security/passkeys/{original.CredentialId}", null, signedIn.AccessToken);
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        var loginOptions = await PostStaffAsync(host, "/api/auth/staff/passkey/options", null);
        using var assertion = JsonDocument.Parse(replacement.Assert(loginOptions.OptionsJson!));
        Assert.NotNull((await PostStaffAsync(host, "/api/auth/staff/passkey/complete",
            new { challenge = loginOptions.Challenge, response = assertion.RootElement })).AccessToken);
    }

    [Fact]
    public async Task RevokeSession_SelectedOwnDevice_PreservesOtherDevicesAndAccounts()
    {
        await using var host = await HostAsync();
        var first = await RegisterTeacherAsync(host, "session-owner");
        using var secondBrowser = host.CreateBrowser();
        using var unrelatedBrowser = host.CreateBrowser();
        var second = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "session-owner", password = Password }, browser: secondBrowser);
        var other = await RegisterTeacherAsync(host, "other-session-owner", unrelatedBrowser);
        var secondSession = await SessionIdAsync(host, second.RefreshToken!);
        using var profile = await host.SendAsync(HttpMethod.Get, "/api/auth/staff/security", null, first.AccessToken);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var security = JsonDocument.Parse(await profile.Content.ReadAsStringAsync(Token));
        var sessions = security.RootElement.GetProperty("sessions");
        Assert.Equal(2, sessions.GetArrayLength());
        Assert.Single(sessions.EnumerateArray(), session => session.GetProperty("isCurrent").GetBoolean());

        using var revoked = await host.SendAsync(HttpMethod.Delete, $"/api/auth/staff/sessions/{secondSession}", null, first.AccessToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var current = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, first.AccessToken);
        using var selected = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, second.AccessToken, secondBrowser);
        using var unrelated = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, other.AccessToken, unrelatedBrowser);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, selected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_CurrentAccount_PreservesOtherAccount()
    {
        await using var host = await HostAsync();
        var current = await RegisterTeacherAsync(host, "all-sessions-owner");
        using var secondBrowser = host.CreateBrowser();
        using var unrelatedBrowser = host.CreateBrowser();
        var second = await PostStaffAsync(host, "/api/auth/staff/password", new { login = "all-sessions-owner", password = Password }, browser: secondBrowser);
        var other = await RegisterTeacherAsync(host, "unrelated-account", unrelatedBrowser);

        using var revoked = await host.SendAsync(HttpMethod.Delete, "/api/auth/staff/sessions", null, current.AccessToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var firstAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, current.AccessToken);
        using var secondAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, second.AccessToken, secondBrowser);
        using var otherAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, other.AccessToken, unrelatedBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, firstAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAccess.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherAccess.StatusCode);
    }

    [Fact]
    public async Task SwitchAccount_StaffToClient_DeniesInactiveAccessAndBackgroundRefresh()
    {
        await using var host = await HostAsync();
        var staff = await RegisterTeacherAsync(host, "switched-teacher");
        var link = await ClientLinkAsync(host);

        var client = await PostClientAsync(host, "/api/auth/client/sign-in", link);

        using var previousAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, staff.AccessToken);
        using var previousRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = staff.RefreshToken });
        using var currentAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/client/me", null, client.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, previousAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, previousRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, currentAccess.StatusCode);
        var reactivated = await PostStaffAsync(host, "/api/auth/staff/resume", new { refreshToken = staff.RefreshToken });
        Assert.NotNull(reactivated.AccessToken);
        using var inactiveClient = await host.SendAsync(HttpMethod.Get, "/api/auth/client/me", null, client.AccessToken);
        using var inactiveClientRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/client/refresh", new { secret = client.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveClient.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveClientRefresh.StatusCode);
    }

    [Fact]
    public async Task Resume_InactiveAdministrator_PreservesCurrentAccountUntilFactorCompletes()
    {
        await using var host = await HostAsync();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "remembered-administrator", authenticator: true);
        var administrator = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge, authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!)
        });
        var teacher = await RegisterTeacherAsync(host, "active-teacher");

        var pending = await PostStaffAsync(host, "/api/auth/staff/resume", new { refreshToken = administrator.RefreshToken });

        Assert.Null(pending.AccessToken);
        Assert.NotNull(pending.Challenge);
        using var beforeFactor = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, teacher.AccessToken);
        using var inactiveAdministrator = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.OK, beforeFactor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveAdministrator.StatusCode);
        var resumed = await PostStaffAsync(host, "/api/auth/staff/factor", new
        {
            challenge = pending.Challenge, code = await CodeAsync(host, registration.AuthenticatorSecret!, futureStep: true)
        });
        using var afterFactor = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, resumed.AccessToken);
        using var previousTeacher = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, teacher.AccessToken);
        Assert.Equal(HttpStatusCode.OK, afterFactor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, previousTeacher.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resume_MissingOrDifferentBrowserContext_CannotActivateRememberedGrant(bool establishedForeignBrowser)
    {
        await using var host = await HostAsync();
        var inactive = await RegisterTeacherAsync(host, "remembered-teacher");
        var current = await RegisterTeacherAsync(host, "current-teacher");
        using var foreignBrowser = host.CreateBrowser(cookies: establishedForeignBrowser);
        StaffCredentialResponse? foreignAccount = null;
        if (establishedForeignBrowser)
            foreignAccount = await RegisterTeacherAsync(host, "foreign-browser-teacher", foreignBrowser);

        using var attempted = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/resume",
            new { refreshToken = inactive.RefreshToken }, browser: foreignBrowser);

        Assert.Equal(HttpStatusCode.Unauthorized, attempted.StatusCode);
        using var stillCurrent = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, current.AccessToken);
        Assert.Equal(HttpStatusCode.OK, stillCurrent.StatusCode);
        if (foreignAccount is not null)
        {
            using var foreignCurrent = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, foreignAccount.AccessToken, foreignBrowser);
            Assert.Equal(HttpStatusCode.OK, foreignCurrent.StatusCode);
        }
    }

    [Fact]
    public async Task ClientLogout_CurrentDevice_RejectsOldSessionAndPreservesOtherDevice()
    {
        await using var host = await HostAsync();
        var link = await ClientLinkAsync(host);
        var first = await PostClientAsync(host, "/api/auth/client/sign-in", link);
        using var secondBrowser = host.CreateBrowser();
        var second = await PostClientAsync(host, "/api/auth/client/sign-in", link, secondBrowser);

        using var logout = await host.SendAsync(HttpMethod.Post, "/api/auth/client/logout", null, first.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var oldAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/client/me", null, first.AccessToken);
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/client/refresh", new { secret = first.RefreshToken });
        using var otherDevice = await host.SendAsync(HttpMethod.Get, "/api/auth/client/me", null, second.AccessToken, secondBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherDevice.StatusCode);
        Assert.NotNull((await PostClientAsync(host, "/api/auth/client/sign-in", link)).AccessToken);
        using var staff = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, second.AccessToken, secondBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, staff.StatusCode);
    }

    [Fact]
    public async Task ClientLink_ReplacedOrClientRestored_NeverRevivesOldAccess()
    {
        await using var host = await HostAsync();
        var original = await ClientLinkAsync(host);
        var session = await PostClientAsync(host, "/api/auth/client/sign-in", original);
        var replacement = await ClientLinkAsync(host);

        using var oldLink = await host.SendAsync(HttpMethod.Post, "/api/auth/client/sign-in", new { secret = original });
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/client/refresh", new { secret = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLink.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var current = await PostClientAsync(host, "/api/auth/client/sign-in", replacement);
        await host.Database.Query("UPDATE public.clients SET is_archived=true WHERE id=@id")
            .WithParameter("id", TestHost.ClientId).ExecuteAsync(Token);
        await host.Database.Query("UPDATE public.clients SET is_archived=false WHERE id=@id")
            .WithParameter("id", TestHost.ClientId).ExecuteAsync(Token);

        using var restoredLink = await host.SendAsync(HttpMethod.Post, "/api/auth/client/sign-in", new { secret = replacement });
        using var restoredSession = await host.SendAsync(HttpMethod.Get, "/api/auth/client/me", null, current.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, restoredLink.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, restoredSession.StatusCode);
        Assert.NotNull((await PostClientAsync(host, "/api/auth/client/sign-in", await ClientLinkAsync(host))).AccessToken);
    }

    private static async Task<StaffCredentialResponse> RegisterTeacherAsync(TestHost host, string login, HttpClient? browser = null)
    {
        var invitation = await InviteAsync(host, "teacher");
        var options = await BeginRegistrationAsync(host, invitation, login, browser: browser);
        return await PostStaffAsync(host, "/api/auth/staff/register/complete", new { challenge = options.Challenge }, browser: browser);
    }

    private static async Task<StaffCredentialResponse> RegisterPasskeyTeacherAsync(TestHost host, string login, WebAuthnTestAuthenticator authenticator)
    {
        var invitation = await InviteAsync(host, "teacher");
        var options = await PostStaffAsync(host, "/api/auth/staff/register/options", new
        {
            link = invitation, login, password = (string?)null, authenticator = false, secondFactor = false
        });
        using var response = JsonDocument.Parse(authenticator.Register(options.OptionsJson!));
        return await PostStaffAsync(host, "/api/auth/staff/register/complete", new { challenge = options.Challenge, passkeyResponse = response.RootElement });
    }

    private static Task<Guid> SessionIdAsync(TestHost host, string refreshToken) =>
        host.Database.Query("SELECT id FROM public.staff_sessions WHERE refresh_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(refreshToken)).GetSingleAsync<Guid>(Token);

    private static async Task<string> InviteAsync(TestHost host, string role) =>
        (await PostStaffAsync(host, "/api/auth/staff/invitations", new { role }, host.SuperToken)).Challenge!;

    private static Task<StaffCredentialResponse> BeginRegistrationAsync(TestHost host, string invitation, string login, bool authenticator = false,
        HttpClient? browser = null) => PostStaffAsync(host, "/api/auth/staff/register/options",
            new { link = invitation, login, password = Password, authenticator, secondFactor = authenticator }, browser: browser);

    private static async Task<StaffCredentialResponse> PostStaffAsync(TestHost host, string path, object? body, string? access = null, HttpClient? browser = null)
    {
        using var response = await host.SendAsync(HttpMethod.Post, path, body, access, browser);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var credentials = (await response.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!;
        if (credentials.RefreshToken is null) AssertNoRefreshCookie(response);
        else AssertRefreshCookie(response, credentials);
        return credentials;
    }

    private static async Task<string> ClientLinkAsync(TestHost host)
    {
        using var response = await host.SendAsync(HttpMethod.Put, $"/api/clients/{TestHost.ClientId}/access-link", new { commandId = Guid.NewGuid() }, host.SuperToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return result.RootElement.GetProperty("secret").GetString()!;
    }

    private static async Task<ClientSessionCredentials> PostClientAsync(TestHost host, string path, string secret, HttpClient? browser = null)
    {
        using var response = await host.SendAsync(HttpMethod.Post, path, new { secret }, browser: browser);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClientSessionCredentials>(Token))!;
    }

    private static async Task<string> CodeAsync(TestHost host, string secret, bool futureStep = false)
    {
        var now = await host.Database.Query("SELECT clock_timestamp()").GetSingleAsync<DateTime>(Token);
        return new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.SpecifyKind(now, DateTimeKind.Utc).AddSeconds(futureStep ? 30 : 0));
    }

    private static void AssertRefreshCookie(HttpResponseMessage response, StaffCredentialResponse credentials)
    {
        Assert.NotNull(credentials.RefreshToken);
        var cookie = Assert.Single(StaffCookies(response));
        Assert.Equal(credentials.RefreshToken, cookie.Value.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/auth", cookie.Path.ToString());
        Assert.Equal(TimeSpan.FromDays(14), cookie.MaxAge);
    }

    private static void AssertNoRefreshCookie(HttpResponseMessage response) => Assert.Empty(StaffCookies(response));

    private static void AssertRefreshCookieCleared(HttpResponseMessage response)
    {
        var cookie = Assert.Single(StaffCookies(response));
        Assert.Equal(string.Empty, cookie.Value.ToString());
        Assert.Equal(DateTimeOffset.UnixEpoch, cookie.Expires);
    }

    private static Microsoft.Net.Http.Headers.SetCookieHeaderValue[] StaffCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(value))
                .Where(cookie => cookie.Name.ToString() == StaffSessionCookies.Name).ToArray()
            : [];

    private async Task<TestHost> HostAsync()
    {
        var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql",
            "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql", "0012_CreateClientAccessSessions.sql", "0013_CreateBrowserAccessContexts.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.clients(id,first_name,last_name,created_at) VALUES (@client,'Synthetic','Client',CURRENT_TIMESTAMP);
            """).WithParameter("client", TestHost.ClientId).ExecuteAsync(Token);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Passkeys:RelyingPartyId"] = "localhost",
            ["Authentication:Passkeys:ServerName"] = "Synthetic school",
            ["Authentication:Passkeys:Origins:0"] = "https://localhost"
        });
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(new PersonalDataProtector("test", new Dictionary<string, string>
        {
            ["test"] = Convert.ToBase64String(Enumerable.Repeat((byte)31, 32).ToArray())
        }));
        builder.Services.AddCurrentStaffAccess();
        builder.Services.AddBrowserAccess();
        builder.Services.AddScoped<IStaffAccessTokenStore, StaffAccessTokenStore>();
        builder.Services.AddStaffAuthentication();
        builder.Services.AddCredentialSecurity();
        builder.Services.AddStaffCredentialFlows();
        builder.Services.AddScoped<IClientAuthenticationStore, ClientAuthenticationStore>();
        builder.Services.AddClientAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapStaffSessionEndpoints();
        app.MapStaffCredentialEndpoints();
        app.MapClientAuthenticationEndpoints();
        TestHost? host = null;
        try
        {
            await app.StartAsync(Token);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            host = new TestHost(app, new HttpClient { BaseAddress = new Uri(address) },
                new HttpClient { BaseAddress = new Uri(address) }, database);
            var invitation = await AuthBootstrapCommand.CreateAsync(database, null, Token);
            Assert.True(invitation.IsSuccess);
            var registration = await PostStaffAsync(host, "/api/auth/staff/register/options", new
            {
                link = invitation.Value, login = "fixture-super", password = Password, authenticator = true, secondFactor = true
            }, browser: host.SuperBrowser);
            var completed = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
            {
                challenge = registration.Challenge, authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!)
            }, browser: host.SuperBrowser);
            Assert.NotNull(completed.AccessToken);
            host.SetSuperToken(completed.AccessToken);
            return host;
        }
        catch
        {
            if (host is null) await app.DisposeAsync();
            else await host.DisposeAsync();
            throw;
        }
    }

    private sealed class TestHost(WebApplication app, HttpClient client, HttpClient superBrowser, Database database) : IAsyncDisposable
    {
        internal static readonly Guid ClientId = Guid.Parse("019a2c63-8c00-7000-8000-000000000002");
        internal Database Database { get; } = database;
        internal HttpClient SuperBrowser { get; } = superBrowser;
        internal string Origin => client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        internal string SuperToken { get; private set; } = "";
        internal void SetSuperToken(string value) => SuperToken = value;
        internal HttpClient CreateBrowser(bool cookies = true) => new(new HttpClientHandler { UseCookies = cookies }) { BaseAddress = client.BaseAddress };
        internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? access = null,
            HttpClient? browser = null, string? origin = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
            return await (browser ?? (access == SuperToken ? SuperBrowser : client)).SendAsync(request, Token);
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            SuperBrowser.Dispose();
            await app.DisposeAsync();
        }
    }
}
