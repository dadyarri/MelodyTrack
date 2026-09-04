using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.SystemNotices;
using MelodyTrack.Backend.Api.SystemNotices.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.SystemNotices.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/system-notices/pre-auth")]
public sealed class GetPreAuthSystemNoticesEndpoint
{
    [AllowAnonymous]
    public static async Task<Ok<GetSystemNoticesResponse>> HandleAsync(
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var notices = await db.SystemNotices
            .AsNoTracking()
            .Where(notice =>
                notice.ShowBeforeAuthentication &&
                notice.AudienceType == SystemNoticeAudienceType.Everyone &&
                (notice.ExpiresAtUtc == null || notice.ExpiresAtUtc > nowUtc))
            .OrderByDescending(notice => notice.Severity)
            .ThenByDescending(notice => notice.CreatedAtUtc)
            .ToListAsync(ct);

        return TypedResults.Ok(new GetSystemNoticesResponse
        {
            Items = notices.Select(SystemNoticeMapper.ToResponse).ToArray()
        });
    }
}
