using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Components.ApiClient;

public class AuthApi(ApiUtils apiUtils)
{
    public async Task<ApiResponse<CheckIf2FaEnabledResponse>> CheckIf2FaEnabledAsync(CheckIf2FaEnabledRequest request)
    {
        return await apiUtils.CallApiAsync<CheckIf2FaEnabledResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"auth/2fa/enabled?{request.ToQueryString()}"),
            true
        );
    }

    public async Task<ApiResponse<CreateInviteResponse>> CreateInviteAsync(CreateInviteRequest request)
    {
        return await apiUtils.CallApiAsync<CreateInviteResponse>(new HttpRequestMessage(HttpMethod.Post, "auth/invite")
        {
            Content = JsonContent.Create(request)
        });
    }

    public async Task<ApiResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        return await apiUtils.CallApiAsync(new HttpRequestMessage(HttpMethod.Post, "auth/forgotPassword")
            {
                Content = JsonContent.Create(request)
            },
            true);
    }


    public async Task<ApiResponse<GetInviteCodeInformationResponse>> GetInviteCodeInformationAsync(GetInviteCodeInformationRequest request)
    {
        return await apiUtils.CallApiAsync<GetInviteCodeInformationResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"/auth/invite?inviteCode={request.InviteCode}"),
            true);
    }

    public async Task<ApiResponse<GetSessionsResponse>> GetSessionsAsync()
    {
        return await apiUtils.CallApiAsync<GetSessionsResponse>(new HttpRequestMessage(HttpMethod.Get, "/auth/sessions"));
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        return await apiUtils.CallApiAsync<LoginResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(request)
            }, true);
    }

    public async Task<ApiResponse> LogoutAllAsync()
    {
        return await apiUtils.CallApiAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/logoutAll"));
    }

    public async Task<ApiResponse> LogoutAsync(LogoutRequest request)
    {
        return await apiUtils.CallApiAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = JsonContent.Create(request)
        });
    }

    public async Task<ApiResponse<Recover2FaResponse>> Recover2FaAsync(Recover2FaRequest request)
    {
        return await apiUtils.CallApiAsync<Recover2FaResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/auth/2fa/recover") { Content = JsonContent.Create(request) },
            true);
    }

    public async Task<ApiResponse<RecoveryCodesResponse>> RecoveryCodesAsync()
    {
        return await apiUtils.CallApiAsync<RecoveryCodesResponse>(new HttpRequestMessage(HttpMethod.Post, "/auth/recoveryCodes"));
    }

    public async Task<ApiResponse<LoginResponse>> RefreshAsync(RefreshRequest request)
    {
        return await apiUtils.CallApiAsync<LoginResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
            {
                Content = JsonContent.Create(request)
            },
            true
        );
    }

    public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request)
    {
        var content = JsonContent.Create(request);
        return await apiUtils.CallApiAsync<RegisterResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/auth/register") { Content = content },
            true
        );
    }

    public async Task<ApiResponse> Remove2FaAsync()
    {
        return await apiUtils.CallApiAsync(new HttpRequestMessage(HttpMethod.Delete, "/auth/2fa/delete"));
    }

    public async Task<ApiResponse> ResetPasswordAsync(ResetPasswordRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Post, "/auth/resetPassword") { Content = JsonContent.Create(request) },
            true
        );
    }

    public async Task<ApiResponse<Setup2FaResponse>> Setup2FaAsync(Setup2FaRequest request)
    {

        return await apiUtils.CallApiAsync<Setup2FaResponse>(
            new HttpRequestMessage(HttpMethod.Post, "/auth/2fa/setup")
            {
                Content = JsonContent.Create(request)
            }
        );
    }

    public async Task<ApiResponse> Verify2FaAsync(Verify2FaRequest request)
    {
        return await apiUtils.CallApiAsync(
            new HttpRequestMessage(HttpMethod.Post, "/auth/2fa/verify")
            {
                Content = JsonContent.Create(request)
            },
            true
        );
    }
}