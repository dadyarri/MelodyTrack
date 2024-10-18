namespace MelodyTrack.ApiService.Storage.Entities;

public class User: BaseEntity
{
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}
