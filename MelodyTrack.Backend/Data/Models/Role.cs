using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class Role: BaseModel
{
    public UserRoles RoleName { get; set; }
    public required string DisplayName { get; set; }
}