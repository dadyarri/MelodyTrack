namespace MelodyTrack.ApiService.Endpoints.Auth.Refresh;

using FastEndpoints;
using Login;

public class RefreshEndpoint : Ep.Req<RefreshRequest>.Res<LoginResponse>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/refresh");
    }

    public override async Task<LoginResponse> HandleAsync(RefreshRequest req, CancellationToken ct)
    {
        return new LoginResponse
        {
            AccessToken = "access-token", RefreshToken = "refresh-token", ValidUntil = DateTime.UtcNow.AddHours(1)
        };
    }
}
