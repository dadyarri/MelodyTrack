namespace MelodyTrack.Backend.Data.Enums;

[Flags]
public enum UserRoles
{
    Superuser = 1,
    Admin = 2,
    User = 4
}

public static class UserRolesExtensions
{
    public static bool IsAnyAdmin(this UserRoles role)
    {
        return (role & (UserRoles.Admin | UserRoles.Superuser)) != 0;
    }
}