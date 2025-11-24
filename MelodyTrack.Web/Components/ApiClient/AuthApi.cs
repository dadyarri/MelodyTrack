using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class AuthApi(ApiUtils apiUtils)
{
    public async Task<ApiResponse<CheckIf2FaEnabledResponse>> CheckIf2FaEnabledAsync(CheckIf2FaEnabledRequest request, NavigationManager nav)
    {
        return await apiUtils.CallApiAsync<CheckIf2FaEnabledResponse>(
            async client => await client.GetAsync($"auth/2fa/enabled?{request.ToQueryString()}"),
            true
        );
    }

    public async Task<ApiResponse<CreateInviteResponse>> CreateInviteAsync(CreateInviteRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<CreateInviteResponse>(
            async client => await client.PostAsync("auth/invite", content)
        );
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, NavigationManager nav)
    {
        var content = JsonContent.Create(request);
        await apiUtils.CallApiAsync(async client => await client.PostAsync("auth/forgotPassword", content),
            true
        );
    }


    public async Task<ApiResponse<GetInviteCodeInformationResponse>> GetInviteCodeInformationAsync(GetInviteCodeInformationRequest request)
    {
        return await apiUtils.CallApiAsync<GetInviteCodeInformationResponse>(
            async client => await client.GetAsync($"/auth/invite?inviteCode={request.InviteCode}"),
            true
        );
    }

    public async Task<ApiResponse<GetSessionsResponse>> GetSessionsAsync(NavigationManager navigationManager)
    {
        return await apiUtils.CallApiAsync<GetSessionsResponse>(
            async client => await client.GetAsync("/auth/sessions")
        );
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<LoginResponse>(
            async client => await client.PostAsync("/auth/login", content),
            true
        );
    }

    public async Task LogoutAllAsync(NavigationManager navigationManager)
    {
        await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/logoutAll", null)
        );
    }

    public async Task LogoutAsync(LogoutRequest request)
    {
        var content = JsonContent.Create(request);

        await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/logout", content)
        );
    }

    public async Task<ApiResponse<Recover2FaResponse>> Recover2FaAsync(Recover2FaRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Recover2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/recover", content),
            true
        );
    }

    public async Task<ApiResponse<RecoveryCodesResponse>> RecoveryCodesAsync()
    {
        return await apiUtils.CallApiAsync<RecoveryCodesResponse>(
            async client => await client.PostAsync("/auth/recoveryCodes", null)
        );
    }

    public async Task<ApiResponse<LoginResponse>> RefreshAsync(RefreshRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<LoginResponse>(
            async client => await client.PostAsync("/auth/refresh", content),
            true
        );
    }

    public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<RegisterResponse>(
            async client => await client.PostAsync("/auth/register", content),
            true
        );
    }

    public async Task<ApiResponse<object>> Remove2FaAsync()
    {
        return await apiUtils.CallApiAsync(
            async client => await client.DeleteAsync("/auth/2fa/delete")
        );
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/resetPassword", content),
            true
        );
    }

    public async Task<ApiResponse<Setup2FaResponse>> Setup2FaAsync(Setup2FaRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync<Setup2FaResponse>(
            async client => await client.PostAsync("/auth/2fa/setup", content)
        );
    }

    public async Task<ApiResponse<object>> Verify2FaAsync(Verify2FaRequest request)
    {
        var content = JsonContent.Create(request);

        return await apiUtils.CallApiAsync(
            async client => await client.PostAsync("/auth/2fa/verify", content),
            true
        );
    }
}