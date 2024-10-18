namespace MelodyTrack.ApiService.Endpoints.Auth.Register;

using FastEndpoints;
using Login;

public class RegisterEndpoint : Ep.Req<RegisterRequest>.Res<LoginResponse>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/register");
        AllowAnonymous();
    }

    public async override Task<LoginResponse> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        return new LoginResponse
        {
            AccessToken = "access-token", RefreshToken = "refresh-token", ValidUntil = DateTime.UtcNow.AddHours(1)
        };
    }
}
