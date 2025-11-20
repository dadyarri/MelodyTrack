using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class AuthApi(ApiUtils apiUtils)
{
    public async Task<(CheckIf2FaEnabledResponse?, HttpResponseMessage)> CheckIf2FaEnabledAsync(CheckIf2FaEnabledRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<CheckIf2FaEnabledResponse>(
            async client => await client.PostAsync("auth/2fa/enabled", content),
            nav,
            true
        );
    }

    public async Task<(CreateInviteResponse?, HttpResponseMessage)> CreateInviteAsync(CreateInviteRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<CreateInviteResponse>(
            async client => await client.PostAsync("auth/invite", content),
            nav
        );
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        await apiUtils.CallApiAsync(async client => await client.PostAsync("auth/forgotPassword", content),
            nav,
            true
        );
    }


    public async Task<(GetInviteCodeInformationResponse?, HttpResponseMessage)> GetInviteCodeInformationAsync(GetInviteCodeInformationRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetInviteCodeInformationResponse>(
            async client => await client.GetAsync($"/auth/invite?inviteCode={request.InviteCode}"),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<(GetSessionsResponse?, HttpResponseMessage)> GetSessionsAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetSessionsResponse>(
            async client => await client.GetAsync("/auth/sessions"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(LoginResponse?, HttpResponseMessage)> LoginAsync(LoginRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<LoginResponse>(
            async client => await client.PostAsync("/auth/login", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task LogoutAllAsync(NavigationManager navigationManager)
    {
        await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/logoutAll", null),
            navigationManager,
            anonymous: false
        );
    }

    public async Task LogoutAsync(LogoutRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/logout", content),
            navigationManager
        );
    }

    public async Task<(Recover2FaResponse?, HttpResponseMessage)> Recover2FaAsync(Recover2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Recover2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/recover", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<(RecoveryCodesResponse?, HttpResponseMessage)> RecoveryCodesAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<RecoveryCodesResponse>(
            async client => await client.PostAsync("/auth/recoveryCodes", null),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<(LoginResponse?, HttpResponseMessage)> RefreshAsync(RefreshRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<LoginResponse>(
            async client => await client.PostAsync("/auth/refresh", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<(RegisterResponse?, HttpResponseMessage)> RegisterAsync(RegisterRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<RegisterResponse>(
            async client => await client.PostAsync("/auth/register", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<HttpResponseMessage> Remove2FaAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync("/auth/2fa/delete"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<HttpResponseMessage> ResetPasswordAsync(ResetPasswordRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/resetPassword", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<(Setup2FaResponse?, HttpResponseMessage)> Setup2FaAsync(Setup2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Setup2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/setup", content),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<HttpResponseMessage> Verify2FaAsync(Verify2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/2fa/verify", content),
            navigationManager,
            anonymous: true
        );
    }
}