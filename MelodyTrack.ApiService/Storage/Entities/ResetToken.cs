namespace MelodyTrack.ApiService.Storage.Entities;

public class ResetToken : BaseEntity
{
    public string Token { get; set; }
    public bool Used { get; set; }
    public User User { get; set; }
    public DateTime ExpireAt { get; set; }
}
