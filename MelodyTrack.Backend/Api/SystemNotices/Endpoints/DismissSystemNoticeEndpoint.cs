using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.SystemNotices.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/system-notices/{id}/dismissals")]
public sealed class DismissSystemNoticeEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> HandleAsync(
        [AsParameters] GetEntityRequest request,
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

        var notice = await db.SystemNotices
            .Include(item => item.Recipients)
            .FirstOrDefaultAsync(item => item.Id == request.Id, ct);
        if (notice is null || !notice.Dismissible || !IsVisibleTo(notice, user))
        {
            return TypedResults.NotFound();
        }

        var recipient = notice.Recipients.FirstOrDefault(item => item.UserId == user.Id);
        if (recipient is null)
        {
            recipient = new SystemNoticeRecipient
            {
                Id = Ulid.NewUlid(),
                Notice = notice,
                NoticeId = notice.Id,
                User = user,
                UserId = user.Id
            };
            await db.SystemNoticeRecipients.AddAsync(recipient, ct);
        }

        recipient.ReadAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
        recipient.DismissedAtUtc = recipient.ReadAtUtc;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static bool IsVisibleTo(SystemNotice notice, User user)
    {
        if (notice.AudienceType == SystemNoticeAudienceType.Everyone)
        {
            return true;
        }

        if (notice.AudienceType == SystemNoticeAudienceType.Staff)
        {
            return !user.Role.RoleName.IsClient();
        }

        if (notice.AudienceType == SystemNoticeAudienceType.Clients)
        {
            return user.Role.RoleName.IsClient();
        }

        return notice.Recipients.Any(item => item.UserId == user.Id || user.ClientId != null && item.ClientId == user.ClientId);
    }
}
