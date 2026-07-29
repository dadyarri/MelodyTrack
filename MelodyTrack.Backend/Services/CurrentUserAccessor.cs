using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface ICurrentUserAccessor
{
    string? Email { get; }
    Ulid? SessionId { get; }
    Task<User?> GetAsync(CancellationToken ct);
}

public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, AppDbContext db) : ICurrentUserAccessor
{
    private Task<User?>? _currentUserTask;

    public string? Email =>
        httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(claim => claim.Type == ClaimTypes.Name)?.Value;

    public Ulid? SessionId
    {
        get
        {
            var sessionId = httpContextAccessor.HttpContext?.User.Claims
                .FirstOrDefault(claim => claim.Type == ClaimTypes.Sid)?.Value;
            return Ulid.TryParse(sessionId, out var parsed) ? parsed : null;
        }
    }

    public Task<User?> GetAsync(CancellationToken ct)
    {
        return _currentUserTask ??= LoadAsync(ct);
    }

    private async Task<User?> LoadAsync(CancellationToken ct)
    {
        var email = Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await db.Users
            .Include(user => user.Role)
            .WhereEmailMatches(email)
            .FirstOrDefaultAsync(ct);
    }
}
