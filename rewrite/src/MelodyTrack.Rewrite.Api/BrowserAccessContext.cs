using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Persistence.Access;

namespace MelodyTrack.Rewrite.Api;

public sealed class BrowserAccessContext(IHttpContextAccessor http, IBrowserAccessStore store, IHostEnvironment environment) : IBrowserAccessContext
{
    public const string CookieName = "mt.browser";
    public Guid? ContextId { get; private set; }
    private bool resolved;

    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (resolved) return;
        resolved = true;
        var context = http.HttpContext ?? throw new InvalidOperationException("A browser HTTP context is required.");
        var secret = context.Request.Cookies[CookieName];
        if (OpaqueTokens.IsValid(secret)) ContextId = await store.ResolveAsync(OpaqueTokens.Hash(secret!), cancellationToken);
    }

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await ResolveAsync(cancellationToken);
        if (ContextId is not null) return;
        var context = http.HttpContext ?? throw new InvalidOperationException("A browser HTTP context is required.");
        var secret = OpaqueTokens.Create();
        ContextId = await store.CreateAsync(OpaqueTokens.Hash(secret), cancellationToken);
        context.Response.Cookies.Append(CookieName, secret, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps || !(environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                || !Loopback(context.Request.Host.Host),
            Path = "/", MaxAge = TimeSpan.FromDays(3650), IsEssential = true
        });
    }

    private static bool Loopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);
}

public static class BrowserAccessExtensions
{
    public static IServiceCollection AddBrowserAccess(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IBrowserAccessStore, BrowserAccessStore>();
        services.AddScoped<BrowserAccessContext>();
        services.AddScoped<IBrowserAccessContext>(provider => provider.GetRequiredService<BrowserAccessContext>());
        return services;
    }
}
