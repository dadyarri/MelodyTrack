using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Passkey_Administrator_RequiresTotpAndRejectsSecondPasskeyAsFactor()
    {
        await using var host = await HostAsync();
        using var authenticator = new WebAuthnTestAuthenticator();
        var invitation = await InviteAsync(host, "administrator");
        var registration = await BeginRegistrationAsync(host, invitation, "passkey-administrator", authenticator: true);
        using var attestation = JsonDocument.Parse(authenticator.Register(registration.OptionsJson!));
        var signup = await PostStaffAsync(host, "/api/auth/staff/register/complete", new
        {
            challenge = registration.Challenge,
            authenticatorCode = await CodeAsync(host, registration.AuthenticatorSecret!),
            passkeyResponse = attestation.RootElement
        });
        Assert.NotNull(signup.AccessToken);
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", new { login = "passkey-administrator" });
        using var assertion = JsonDocument.Parse(authenticator.Assert(options.OptionsJson!));

        var primary = await PostStaffAsync(host, "/api/auth/staff/passkey/complete", new { challenge = options.Challenge, response = assertion.RootElement });

        Assert.Null(primary.AccessToken);
        Assert.Equal("authenticator", primary.Factor);
        Assert.Null(primary.OptionsJson);
        using var wrongFactor = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/factor",
            new { challenge = primary.Challenge, passkeyResponse = assertion.RootElement });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongFactor.StatusCode);

        var freshOptions = await PostStaffAsync(host, "/api/auth/staff/passkey/options", new { login = "passkey-administrator" });
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
        var options = await PostStaffAsync(host, "/api/auth/staff/passkey/options", new { login = "passkey-only" });
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
        var loginOptions = await PostStaffAsync(host, "/api/auth/staff/passkey/options", new { login = "two-passkeys" });
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

        var client = await PostClientAsync(host, "/api/client-auth/sign-in", link);

        using var previousAccess = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, staff.AccessToken);
        using var previousRefresh = await host.SendAsync(HttpMethod.Post, "/api/auth/staff/refresh", new { refreshToken = staff.RefreshToken });
        using var currentAccess = await host.SendAsync(HttpMethod.Get, "/api/client-auth/me", null, client.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, previousAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, previousRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, currentAccess.StatusCode);
        var reactivated = await PostStaffAsync(host, "/api/auth/staff/resume", new { refreshToken = staff.RefreshToken });
        Assert.NotNull(reactivated.AccessToken);
        using var inactiveClient = await host.SendAsync(HttpMethod.Get, "/api/client-auth/me", null, client.AccessToken);
        using var inactiveClientRefresh = await host.SendAsync(HttpMethod.Post, "/api/client-auth/refresh", new { secret = client.RefreshToken });
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
        var first = await PostClientAsync(host, "/api/client-auth/sign-in", link);
        using var secondBrowser = host.CreateBrowser();
        var second = await PostClientAsync(host, "/api/client-auth/sign-in", link, secondBrowser);

        using var logout = await host.SendAsync(HttpMethod.Post, "/api/client-auth/logout", null, first.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var oldAccess = await host.SendAsync(HttpMethod.Get, "/api/client-auth/me", null, first.AccessToken);
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/client-auth/refresh", new { secret = first.RefreshToken });
        using var otherDevice = await host.SendAsync(HttpMethod.Get, "/api/client-auth/me", null, second.AccessToken, secondBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherDevice.StatusCode);
        Assert.NotNull((await PostClientAsync(host, "/api/client-auth/sign-in", link)).AccessToken);
        using var staff = await host.SendAsync(HttpMethod.Get, "/api/auth/me", null, second.AccessToken, secondBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, staff.StatusCode);
    }

    [Fact]
    public async Task ClientLink_ReplacedOrClientRestored_NeverRevivesOldAccess()
    {
        await using var host = await HostAsync();
        var original = await ClientLinkAsync(host);
        var session = await PostClientAsync(host, "/api/client-auth/sign-in", original);
        var replacement = await ClientLinkAsync(host);

        using var oldLink = await host.SendAsync(HttpMethod.Post, "/api/client-auth/sign-in", new { secret = original });
        using var oldRefresh = await host.SendAsync(HttpMethod.Post, "/api/client-auth/refresh", new { secret = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLink.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var current = await PostClientAsync(host, "/api/client-auth/sign-in", replacement);
        await host.Database.Query("UPDATE public.clients SET is_archived=true WHERE id=@id")
            .WithParameter("id", TestHost.ClientId).ExecuteAsync(Token);
        await host.Database.Query("UPDATE public.clients SET is_archived=false WHERE id=@id")
            .WithParameter("id", TestHost.ClientId).ExecuteAsync(Token);

        using var restoredLink = await host.SendAsync(HttpMethod.Post, "/api/client-auth/sign-in", new { secret = replacement });
        using var restoredSession = await host.SendAsync(HttpMethod.Get, "/api/client-auth/me", null, current.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, restoredLink.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, restoredSession.StatusCode);
        Assert.NotNull((await PostClientAsync(host, "/api/client-auth/sign-in", await ClientLinkAsync(host))).AccessToken);
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

    private static async Task<StaffCredentialResponse> PostStaffAsync(TestHost host, string path, object body, string? access = null, HttpClient? browser = null)
    {
        using var response = await host.SendAsync(HttpMethod.Post, path, body, access, browser);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffCredentialResponse>(Token))!;
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
        internal string SuperToken { get; private set; } = "";
        internal void SetSuperToken(string value) => SuperToken = value;
        internal HttpClient CreateBrowser(bool cookies = true) => new(new HttpClientHandler { UseCookies = cookies }) { BaseAddress = client.BaseAddress };
        internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? access = null, HttpClient? browser = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            if (access is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
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
