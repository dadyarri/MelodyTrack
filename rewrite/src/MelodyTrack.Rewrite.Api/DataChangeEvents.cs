using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using MelodyTrack.Rewrite.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Rewrite.Api;

/// <summary>Process-local invalidation notifications. Clients always perform an authorized read for current data.</summary>
public sealed class DataChangeEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<long>> subscribers = new();
    private long sequence;

    public long CurrentSequence => Interlocked.Read(ref sequence);

    public void Publish()
    {
        var next = Interlocked.Increment(ref sequence);
        foreach (var subscriber in subscribers.Values)
            subscriber.Writer.TryWrite(next);
    }

    public async IAsyncEnumerable<long> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        if (!subscribers.TryAdd(id, channel)) throw new InvalidOperationException("Could not register data change subscriber.");

        try
        {
            // Every (re)connection starts with an invalidation, covering changes missed while offline.
            yield return CurrentSequence;
            await foreach (var value in channel.Reader.ReadAllAsync(cancellationToken))
                yield return value;
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}

public sealed record DataChangeEventOptions(TimeSpan HeartbeatInterval);

public sealed class DataChangeEventsMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SessionOperationPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/staff/refresh", "/api/auth/staff/restore", "/api/auth/staff/resume",
        "/api/auth/staff/password", "/api/auth/staff/passkey/options", "/api/auth/staff/passkey/complete",
        "/api/auth/staff/factor", "/api/auth/logout", "/api/auth/client/sign-in",
        "/api/auth/client/refresh", "/api/auth/client/activate", "/api/auth/client/logout"
    };

    public async Task InvokeAsync(HttpContext context, DataChangeEventHub events)
    {
        if (!IsMutation(context.Request))
        {
            await next(context);
            return;
        }

        await next(context);
        if (context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices)
            events.Publish();
    }

    private static bool IsMutation(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api")) return false;
        if (request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return false;

        // These endpoints establish, refresh or end a browser session. Broadcasting them would make clients
        // refetch session state in response to the refresh operation itself.
        var path = request.Path.Value?.TrimEnd('/');
        return path is null || !SessionOperationPaths.Contains(path);
    }
}

public static class DataChangeEventExtensions
{
    public const string AuthenticationScheme = "DataChangeBearer";
    public const string AnyAuthenticatedPolicy = "DataChangesAuthenticated";

    public static IServiceCollection AddDataChangeEvents(this IServiceCollection services)
    {
        services.AddSingleton<DataChangeEventHub>();
        services.AddSingleton(new DataChangeEventOptions(TimeSpan.FromSeconds(30)));
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DataChangeAuthenticationHandler>(AuthenticationScheme, _ => { });
        services.AddAuthorization(options => options.AddPolicy(AnyAuthenticatedPolicy, policy =>
        {
            policy.AddAuthenticationSchemes(AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => context.User.HasClaim(StaffIdentity.ActorKindClaim, "staff")
                || context.User.HasClaim(StaffIdentity.ActorKindClaim, "client"));
        }));
        return services;
    }

    public static IApplicationBuilder UseDataChangeEvents(this IApplicationBuilder app) =>
        app.UseMiddleware<DataChangeEventsMiddleware>();

    public static IEndpointRouteBuilder MapDataChangeEvents(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/changes/events", StreamAsync)
            .RequireAuthorization(AnyAuthenticatedPolicy)
            .WithName("StreamDataChanges")
            .WithDescription("Streams invalidation notifications. Every connection starts with an invalidation; fetch current data through authorized API routes.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return endpoints;
    }

    private static async Task StreamAsync(HttpContext context, DataChangeEventHub events,
        IServiceScopeFactory scopeFactory, DataChangeEventOptions options)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var cancellationToken = streamCancellation.Token;
        await using var subscription = events.Subscribe(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var nextEvent = subscription.MoveNextAsync().AsTask();
        using var heartbeatTimer = new PeriodicTimer(options.HeartbeatInterval);
        var heartbeat = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();
        try
        {
            while (true)
            {
                _ = await Task.WhenAny(nextEvent, heartbeat);
                if (heartbeat.IsCompleted)
                {
                    if (!await heartbeat) break;
                    await context.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    if (!await IsSessionActiveAsync(context.User, context.Request.Headers.Authorization, scopeFactory, cancellationToken))
                        return;
                    heartbeat = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                    continue;
                }

                if (!await nextEvent) break;
                var sequence = subscription.Current;
                await context.Response.WriteAsync($"id: {sequence}\nevent: invalidate\ndata: {sequence}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
                nextEvent = subscription.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            streamCancellation.Cancel();
            try { await nextEvent; }
            catch (OperationCanceledException) when (streamCancellation.IsCancellationRequested) { }
        }
    }

    private static async Task<bool> IsSessionActiveAsync(ClaimsPrincipal principal, string? authorization,
        IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter)) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BrowserAccessContext>().ResolveAsync(cancellationToken);
        if (principal.HasClaim(StaffIdentity.ActorKindClaim, "staff"))
        {
            var identity = await scope.ServiceProvider.GetRequiredService<StaffAccessTokens>()
                .AuthenticateAsync(header.Parameter, cancellationToken);
            return identity is not null
                && principal.FindFirstValue(ClaimTypes.NameIdentifier) == identity.StaffId.Value.ToString();
        }

        if (principal.HasClaim(StaffIdentity.ActorKindClaim, "client"))
        {
            var identity = await scope.ServiceProvider.GetRequiredService<ClientAuthentication>()
                .AuthenticateAsync(header.Parameter, cancellationToken);
            return identity is not null
                && principal.FindFirstValue(ClientAuthenticationHandler.SessionClaim) == identity.SessionId.ToString();
        }

        return false;
    }
}

public sealed class DataChangeAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var staff = await Context.AuthenticateAsync(StaffAuthentication.SchemeName);
        if (staff.Succeeded) return staff;

        var client = await Context.AuthenticateAsync(ClientAuthenticationHandler.SchemeName);
        return client.Succeeded || client.Failure is not null ? client : staff;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Требуется вход").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Доступ запрещён").ExecuteAsync(Context);
    }
}
