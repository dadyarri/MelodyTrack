using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface ICurrentUserAccessor
{
    Task<User?> GetAsync(CancellationToken ct);
}

public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, AppDbContext db) : ICurrentUserAccessor
{
    public async Task<User?> GetAsync(CancellationToken ct)
    {
        var email = httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(e => e.Type == ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .WhereEmailMatches(email)
            .FirstOrDefaultAsync(ct);
    }
}
