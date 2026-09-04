using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.SystemNotices;
using MelodyTrack.Backend.Api.SystemNotices.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.SystemNotices.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/system-notices")]
public sealed class GetSystemNoticesEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<GetSystemNoticesResponse>, UnauthorizedHttpResult>> HandleAsync(
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var isClient = user.Role.RoleName.IsClient();
        var notices = await db.SystemNotices
            .AsNoTracking()
            .Where(notice =>
                (notice.ExpiresAtUtc == null || notice.ExpiresAtUtc > nowUtc) &&
                (notice.AudienceType == SystemNoticeAudienceType.Everyone ||
                 notice.AudienceType == (isClient ? SystemNoticeAudienceType.Clients : SystemNoticeAudienceType.Staff) ||
                 notice.AudienceType == SystemNoticeAudienceType.SpecificRecipients &&
                 notice.Recipients.Any(recipient => recipient.UserId == user.Id ||
                     user.ClientId != null && recipient.ClientId == user.ClientId)) &&
                !notice.Recipients.Any(recipient => recipient.UserId == user.Id && recipient.DismissedAtUtc != null))
            .OrderByDescending(notice => notice.Severity)
            .ThenByDescending(notice => notice.CreatedAtUtc)
            .ToListAsync(ct);

        return TypedResults.Ok(new GetSystemNoticesResponse
        {
            Items = notices.Select(SystemNoticeMapper.ToResponse).ToArray()
        });
    }
}
