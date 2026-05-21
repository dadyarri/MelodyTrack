namespace MelodyTrack.Backend.Data.Models;

public class UserVacation : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
}
