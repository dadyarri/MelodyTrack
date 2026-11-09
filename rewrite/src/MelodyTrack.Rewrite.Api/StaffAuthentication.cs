using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MelodyTrack.Rewrite.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Rewrite.Api;

public sealed class StaffAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, StaffAccessTokens tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StaffBearer";
    internal const string SessionProperty = "melodytrack.session_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count == 0) return AuthenticateResult.NoResult();
        if (headers.Count != 1 || !AuthenticationHeaderValue.TryParse(headers[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
            return AuthenticateResult.Fail("Invalid credentials.");
        var session = await tokens.AuthenticateAsync(header.Parameter, Context.RequestAborted);
        if (session is null || session.StaffId.Value == Guid.Empty || session.SessionId == Guid.Empty)
            return AuthenticateResult.Fail("Invalid credentials.");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, session.StaffId.Value.ToString()),
            new Claim(StaffIdentity.ActorKindClaim, "staff")], SchemeName);
        var properties = new AuthenticationProperties();
        properties.Items[SessionProperty] = session.SessionId.ToString();
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, SchemeName));
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
}
