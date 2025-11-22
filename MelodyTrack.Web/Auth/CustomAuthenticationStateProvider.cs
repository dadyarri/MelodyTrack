using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Web.Components.ApiClient;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MelodyTrack.Web.Auth;

public class CustomAuthenticationStateProvider(Api api, ProtectedLocalStorage localStorage) : AuthenticationStateProvider
{

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new ClaimsIdentity();
        var refreshToken = await localStorage.GetAsync<string>("refresh_token");
        if (await api.Utils.IsAuthenticatedAsync())
        {
            var accessToken = (await localStorage.GetAsync<string>("access_token")).Value!;
            identity = GetClaimsFromJwt(accessToken);
        }
        else if (refreshToken.Value is not null)
        {
            await api.Auth.RefreshAsync(new RefreshRequest { RefreshToken = refreshToken.Value }, null);
        }

        var user = new ClaimsPrincipal(identity);
        return new AuthenticationState(user);
    }

    private static ClaimsIdentity GetClaimsFromJwt(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        var claims = token.Claims;

        return new ClaimsIdentity(claims, "jwt");
    }
}