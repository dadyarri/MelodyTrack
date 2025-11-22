using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class AuthApi(ApiUtils apiUtils)
{
    public async Task<ApiResponse<CheckIf2FaEnabledResponse>> CheckIf2FaEnabledAsync(CheckIf2FaEnabledRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<CheckIf2FaEnabledResponse>(
            async client => await client.PostAsync("auth/2fa/enabled", content),
            nav,
            true
        );
    }

    public async Task<ApiResponse<CreateInviteResponse>> CreateInviteAsync(CreateInviteRequest request, NavigationManager nav)
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


    public async Task<ApiResponse<GetInviteCodeInformationResponse>> GetInviteCodeInformationAsync(GetInviteCodeInformationRequest request, NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetInviteCodeInformationResponse>(
            async client => await client.GetAsync($"/auth/invite?inviteCode={request.InviteCode}"),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<ApiResponse<GetSessionsResponse>> GetSessionsAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetSessionsResponse>(
            async client => await client.GetAsync("/auth/sessions"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request, NavigationManager navigationManager)
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

    public async Task<ApiResponse<Recover2FaResponse>> Recover2FaAsync(Recover2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Recover2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/recover", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<ApiResponse<RecoveryCodesResponse>> RecoveryCodesAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<RecoveryCodesResponse>(
            async client => await client.PostAsync("/auth/recoveryCodes", null),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<ApiResponse<LoginResponse>> RefreshAsync(RefreshRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<LoginResponse>(
            async client => await client.PostAsync("/auth/refresh", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<RegisterResponse>(
            async client => await client.PostAsync("/auth/register", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<ApiResponse<object>> Remove2FaAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync("/auth/2fa/delete"),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/resetPassword", content),
            navigationManager,
            anonymous: true
        );
    }

    public async Task<ApiResponse<Setup2FaResponse>> Setup2FaAsync(Setup2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Setup2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/setup", content),
            navigationManager,
            anonymous: false
        );
    }

    public async Task<ApiResponse<object>> Verify2FaAsync(Verify2FaRequest request, NavigationManager navigationManager)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/2fa/verify", content),
            navigationManager,
            anonymous: true
        );
    }
}