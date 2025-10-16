namespace MelodyTrack.Backend.Data.Enums;

[Flags]
public enum UserRoles
{
    Superuser = 1,
    Admin = 2,
    User = 4
}