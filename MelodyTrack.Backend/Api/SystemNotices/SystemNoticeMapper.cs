using MelodyTrack.Backend.Api.SystemNotices.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.SystemNotices;

public static class SystemNoticeMapper
{
    public static SystemNoticeResponse ToResponse(this SystemNotice notice) => new()
    {
        Id = notice.Id,
        Title = notice.Title,
        Body = notice.Body,
        Severity = notice.Severity.ToString().ToLowerInvariant(),
        CreatedAtUtc = notice.CreatedAtUtc,
        ExpiresAtUtc = notice.ExpiresAtUtc,
        Dismissible = notice.Dismissible,
        AudienceType = notice.AudienceType.ToString().ToLowerInvariant(),
        ShowBeforeAuthentication = notice.ShowBeforeAuthentication
    };
}
