namespace MelodyTrack.Backend.Data.Models;

public class SystemNoticeRecipient : BaseModel
{
    public required Ulid NoticeId { get; set; }
    public required SystemNotice Notice { get; set; }
    public Ulid? UserId { get; set; }
    public User? User { get; set; }
    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
}
