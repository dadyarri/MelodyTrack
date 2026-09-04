using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class SystemNotice : BaseModel
{
    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(4000)]
    public required string Body { get; set; }

    public required SystemNoticeSeverity Severity { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool Dismissible { get; set; }
    public required SystemNoticeAudienceType AudienceType { get; set; }
    public bool ShowBeforeAuthentication { get; set; }
    public List<SystemNoticeRecipient> Recipients { get; set; } = [];
}
