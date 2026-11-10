using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MelodyTrack.Rewrite.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Rewrite.Api;

public sealed class ClientAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ClientAuthentication authentication, BrowserAccessContext browser) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ClientBearer";
    internal const string SessionClaim = "melodytrack.session_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count == 0) return AuthenticateResult.NoResult();
        if (headers.Count != 1 || !AuthenticationHeaderValue.TryParse(headers[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
            return AuthenticateResult.Fail("Invalid credentials.");
        await browser.ResolveAsync(Context.RequestAborted);
        var identity = await authentication.AuthenticateAsync(header.Parameter, Context.RequestAborted);
        if (identity is null || identity.ClientId.Value == Guid.Empty || identity.SessionId == Guid.Empty)
            return AuthenticateResult.Fail("Invalid credentials.");
        var claims = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, identity.ClientId.Value.ToString()),
            new Claim(StaffIdentity.ActorKindClaim, "client"), new Claim(SessionClaim, identity.SessionId.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: 401, title: "Требуется вход").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: 403, title: "Доступ запрещён").ExecuteAsync(Context);
    }

    internal static ClientSessionIdentity ReadIdentity(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (identities.Length != 1 || identities[0].AuthenticationType != SchemeName)
            throw new InvalidOperationException("Client authentication policy was not evaluated.");
        var identity = identities[0];
        return new(new(Guid.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)!.Value)), Guid.Parse(identity.FindFirst(SessionClaim)!.Value));
    }
}
