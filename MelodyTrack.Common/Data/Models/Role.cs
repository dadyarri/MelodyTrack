using MelodyTrack.Common.Data.Enums;

namespace MelodyTrack.Common.Data.Models;

public class Role : BaseModel
{
    public UserRoles RoleName { get; set; }
    public required string DisplayName { get; set; }
}