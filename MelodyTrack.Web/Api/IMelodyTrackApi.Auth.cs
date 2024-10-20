namespace MelodyTrack.Web.Api;

using Common.Contracts.Auth.InitiateReset;
using Common.Contracts.Auth.Login;
using Common.Contracts.Auth.Me;
using Common.Contracts.Auth.Refresh;
using Common.Contracts.Auth.Register;
using Common.Contracts.Auth.Reset;
using Refit;

public partial interface IMelodyTrackApi
{
    [Post("/api/auth/reset")]
    public Task InitiateResetAsync(InitiateResetRequest request);

    [Post("/api/auth/login")]
    public Task LoginAsync(LoginRequest request);

    [Get("/api/auth/me")]
    public Task<MeResponse> MeAsync();

    [Post("/api/auth/refresh")]
    public Task<LoginResponse> RefreshAsync(RefreshRequest request);

    [Post("/api/auth/register")]
    public Task<LoginResponse> RegisterAsync(RegisterRequest request);

    [Post("/api/auth/reset/confirm")]
    public Task<LoginResponse> ResetAsync(ResetRequest request);
}
