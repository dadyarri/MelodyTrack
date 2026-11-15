namespace MelodyTrack.Rewrite.Api;

public static class StaffSessionCookies
{
    public const string Name = "mt.staff.refresh";

    public static void Write(HttpContext context, string refreshToken)
    {
        var options = Options(context);
        options.MaxAge = TimeSpan.FromDays(14);
        context.Response.Cookies.Append(Name, refreshToken, options);
    }

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(Name, Options(context));

    public static bool HasTrustedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        var normalized = uri.GetLeftPart(UriPartial.Authority);
        var own = $"{context.Request.Scheme}://{context.Request.Host}";
        if (Uri.TryCreate(own, UriKind.Absolute, out var ownUri)
            && string.Equals(normalized, ownUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return true;
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetSection("Authentication:Passkeys:Origins").Get<string[]>()?.Any(allowed =>
            string.Equals(normalized, allowed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static CookieOptions Options(HttpContext context)
    {
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var loopback = context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || System.Net.IPAddress.TryParse(context.Request.Host.Host, out var address) && System.Net.IPAddress.IsLoopback(address);
        return new()
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api/auth", IsEssential = true,
            Secure = context.Request.IsHttps || !loopback || !(environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        };
    }
}
