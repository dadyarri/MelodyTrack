using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodyTrack.Rewrite.Api.Tests;

internal sealed class StaffApiHost(WebApplication application, HttpClient client,
    StaffApiHost.AccessState access, StaffApiHost.TokenState tokens, StaffApiHost.Clock clock,
    StaffApiHost.LoginState logins) : IAsyncDisposable
{
    internal static readonly StaffId Staff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    internal static readonly StaffId OtherStaff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    // Synthetic credentials exist only inside this isolated host.
    internal static readonly string BearerToken = new('A', 64);
    internal static readonly string OtherSessionToken = new('C', 64);
    internal static readonly string SameSessionToken = new('D', 64);
    internal static readonly string OtherStaffToken = new('E', 64);
    internal static readonly Guid Session = Guid.Parse("019a2c63-8c00-7000-8000-000000000011");
    internal static readonly Guid OtherSession = Guid.Parse("019a2c63-8c00-7000-8000-000000000012");
    internal static readonly Guid OtherStaffSession = Guid.Parse("019a2c63-8c00-7000-8000-000000000013");
    internal HttpClient Client { get; } = client;
    internal AccessState Access { get; } = access;
    internal TokenState Tokens { get; } = tokens;
    internal Clock Time { get; } = clock;
    internal LoginState Logins { get; } = logins;

    internal static async Task<StaffApiHost> StartAsync(Actor.Staff actor, CancellationToken cancellationToken,
        NotesState? notes = null, ExpenseState? expenses = null, DateTimeOffset? now = null,
        IClientDirectoryStore? directory = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var access = new AccessState(actor);
        var tokens = new TokenState();
        var logins = new LoginState();
        var clock = new Clock(now ?? new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        builder.Services.AddSingleton<ICurrentStaffAccess>(access);
        builder.Services.AddSingleton<IStaffPolicyStore>(access);
        builder.Services.AddSingleton<StaffPolicies>();
        builder.Services.AddSingleton<IStaffAccessTokenStore>(tokens);
        builder.Services.AddSingleton<ICurrentStaffLogin>(logins);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IClientNotesStore>(notes ?? new NotesState(null));
        builder.Services.AddSingleton<ClientNotesService>();
        builder.Services.AddSingleton<IExpenseStore>(expenses ?? new ExpenseState());
        builder.Services.AddSingleton<ExpenseService>();
        if (directory is not null)
        {
            builder.Services.AddSingleton(directory);
            builder.Services.AddSingleton<ClientDirectoryService>();
        }
        builder.Services.AddBrowserAccess();
        builder.Services.AddSingleton<IBrowserAccessStore, BrowserState>();
        builder.Services.AddStaffAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapClientNotesEndpoints();
        app.MapExpensesEndpoints();
        app.MapStaffSessionEndpoints();
        app.MapStaffPoliciesEndpoints();
        if (directory is not null) app.MapClientDirectoryEndpoints();
        app.MapGet("/_test/identity", (ClaimsPrincipal principal, CurrentStaff current) =>
            TypedResults.Ok(new IdentityProbe(current.Actor.Id.Value,
                principal.Claims.Select(claim => new ClaimProbe(claim.Type, claim.Value)).ToArray())));
        try
        {
            await app.StartAsync(cancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, access, tokens, clock, logins);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body,
        CancellationToken cancellationToken, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, body.GetType()) };
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
        return await Client.SendAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
    }

    internal sealed record ClaimProbe(string Type, string Value);
    internal sealed record IdentityProbe(Guid StaffId, ClaimProbe[] Claims);

    internal sealed class LoginState : ICurrentStaffLogin
    {
        internal Dictionary<StaffId, string> Values { get; } = new()
        {
            [Staff] = "fixture-staff",
            [OtherStaff] = "other-staff"
        };
        internal List<StaffId> Reads { get; } = [];

        public Task<Result<string, ErrorList>> GetLoginAsync(StaffId staffId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add(staffId);
            return Task.FromResult(Values.TryGetValue(staffId, out var login)
                ? Result.Success<string, ErrorList>(login)
                : Result.Failure<string, ErrorList>(ErrorList.Single(new ModelError.NotFound())));
        }
    }

    private sealed class BrowserState : IBrowserAccessStore
    {
        private readonly ConcurrentDictionary<string, Guid> contexts = new();
        public Task<Guid?> ResolveAsync(string secretHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(contexts.TryGetValue(secretHash, out var id) ? (Guid?)id : null);
        }
        public Task<Guid> CreateAsync(string secretHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(contexts.GetOrAdd(secretHash, _ => Guid.NewGuid()));
        }
    }

    internal sealed record PolicyWrite(Guid CommandId, StaffId AuthorId, StaffRole? Role,
        StaffId? TargetId, StaffPermission Permission, PermissionGrant? Grant);

    internal sealed class AccessState(Actor.Staff actor) : IStaffPolicyStore
    {
        internal Actor.Staff Actor = actor;
        internal int Reads;
        internal List<PolicyWrite> Writes { get; } = [];
        internal Dictionary<StaffId, Actor.Staff> OtherActors { get; } = [];
        internal ErrorList? WriteError { get; set; }
        public Task<Result<Actor.Staff, ErrorList>> ResolveAsync(StaffId staffId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Reads);
            return Task.FromResult(Result.Success<Actor.Staff, ErrorList>(OtherActors.GetValueOrDefault(staffId, Actor)));
        }

        public Task<UnitResult<ErrorList>> SetRolePolicyAsync(Guid commandId, StaffId authorId, StaffRole role,
            StaffPermission permission, PermissionGrant grant, CancellationToken cancellationToken) =>
            WriteAsync(new(commandId, authorId, role, null, permission, grant), cancellationToken);

        public Task<UnitResult<ErrorList>> SetPersonalPolicyAsync(Guid commandId, StaffId authorId, StaffId targetId,
            StaffPermission permission, PermissionGrant? grant, CancellationToken cancellationToken) =>
            WriteAsync(new(commandId, authorId, null, targetId, permission, grant), cancellationToken);

        private Task<UnitResult<ErrorList>> WriteAsync(PolicyWrite write, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(write);
            return Task.FromResult(WriteError is { } error ? UnitResult.Failure(error) : UnitResult.Success<ErrorList>());
        }
    }

    internal sealed class TokenState : IStaffAccessTokenStore
    {
        internal bool Revoked;
        internal int Reads;
        internal Guid? LastRevokedSession { get; private set; }
        private readonly HashSet<Guid> revokedSessions = [];
        private static readonly string ExpectedHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(BearerToken)));
        private static readonly string OtherSessionHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(OtherSessionToken)));
        private static readonly string SameSessionHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(SameSessionToken)));
        private static readonly string OtherStaffHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(OtherStaffToken)));
        public Task<StaffSessionIdentity?> FindStaffAsync(string tokenHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Reads);
            var session = tokenHash == ExpectedHash || tokenHash == SameSessionHash ? Session
                : tokenHash == OtherSessionHash ? OtherSession
                : tokenHash == OtherStaffHash ? OtherStaffSession : (Guid?)null;
            return Task.FromResult<StaffSessionIdentity?>(!Revoked && session is { } id && !revokedSessions.Contains(id)
                ? new(tokenHash == OtherStaffHash ? OtherStaff : Staff, id) : null);
        }
        public Task<UnitResult<ErrorList>> AddAsync(string tokenHash, StaffId staffId, Guid sessionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP tests must not issue credentials.");
        public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            revokedSessions.Add(sessionId);
            LastRevokedSession = sessionId;
            return Task.CompletedTask;
        }
    }

    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class NotesState(string? initial) : IClientNotesStore
    {
        internal string? Current = initial;
        internal int Reads;
        internal List<ClientNotesChange> History { get; } = [];
        public Task<Result<ClientNotesChange, ErrorList>> ChangeAsync(ClientNotesWriteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            var previous = History.SingleOrDefault(item => item.Id == command.EntryId);
            if (previous is not null)
                return Task.FromResult(previous.ClientId == command.ClientId && previous.AuthorId == command.AuthorId
                    && previous.After == command.Notes && (!command.HasExpectedState || previous.Before == command.ExpectedNotes)
                    ? Result.Success<ClientNotesChange, ErrorList>(previous)
                    : Result.Failure<ClientNotesChange, ErrorList>(ErrorList.Single(new ModelError.InvalidState(), "id")));
            if (command.HasExpectedState && Current != command.ExpectedNotes)
                return Task.FromResult(Result.Failure<ClientNotesChange, ErrorList>(ErrorList.Single(new ModelError.InvalidState(), "notes")));
            var change = ClientNotesChange.Create(command.EntryId, command.ClientId, command.AuthorId, command.ChangedAt, Current, command.Notes);
            return change.IsFailure ? Task.FromResult(change) : AppendAsync(change.Value, cancellationToken);
        }
        public Task<Result<string?, ErrorList>> GetCurrentAsync(ClientId clientId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<string?, ErrorList>(Current));
        }
        public Task<Result<IReadOnlyList<ClientNotesChange>, ErrorList>> GetHistoryAsync(ClientId clientId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<ClientNotesChange>, ErrorList>(History));
        public Task<Result<ClientNotesChange?, ErrorList>> FindChangeAsync(Guid entryId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<ClientNotesChange?, ErrorList>(History.SingleOrDefault(item => item.Id == entryId)));
        }
        public Task<Result<ClientNotesChange, ErrorList>> AppendAsync(ClientNotesChange change, CancellationToken cancellationToken)
        {
            Current = change.After;
            History.Add(change);
            return Task.FromResult(Result.Success<ClientNotesChange, ErrorList>(change));
        }
    }

    internal sealed class ExpenseState : IExpenseStore
    {
        internal Expense? Current;
        internal ExpenseChange? Last;
        internal int Writes;
        public Task<Result<Expense?, ErrorList>> FindAsync(ExpenseId id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP writes must not read an expense before authorization.");
        public Task<Result<ExpenseChange, ErrorList>> ApplyAsync(Guid commandId, ExpenseChange change, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            if (change.Before != Current)
                return Task.FromResult(Result.Failure<ExpenseChange, ErrorList>(ErrorList.Single(new ModelError.InvalidState(), "expense")));
            Current = change.After;
            Last = change;
            return Task.FromResult(Result.Success<ExpenseChange, ErrorList>(change));
        }
    }
}
