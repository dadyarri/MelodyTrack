namespace MelodyTrack.ApiService.Storage.Entities;

public class BannedToken : BaseEntity
{
    public required string Jti { get; set; }
    public required User User { get; set; }
}