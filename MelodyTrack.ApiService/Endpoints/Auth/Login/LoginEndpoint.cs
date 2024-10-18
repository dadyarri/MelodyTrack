namespace MelodyTrack.ApiService.Endpoints.Auth.Login;

using FastEndpoints;

public class LoginEndpoint : Ep.Req<LoginRequest>.Res<LoginResponse>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/login");
        AllowAnonymous();
    }

    public async override Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        return new LoginResponse
        {
            AccessToken = "access-token", RefreshToken = "refresh-token", ValidUntil = DateTime.UtcNow.AddHours(1)
        };
    }
}
