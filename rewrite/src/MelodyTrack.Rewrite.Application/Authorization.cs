using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public static class Authorization
{
    public static bool IsSuperuser(this Actor actor) => actor is Actor.Staff { Role: StaffRole.Superuser };
    public static bool IsReception(this Actor actor) => actor is Actor.Staff { Role: StaffRole.Superuser or StaffRole.Administrator };
}

public static class RecordEditing
{
    // This constrains time; it does not grant a role permission to edit a financial record.
    public static bool IsWithinAllowedMonth(StaffRole role, DateTimeOffset recordAt, DateTimeOffset now) =>
        role == StaffRole.Superuser || SchoolTime.SameMonth(recordAt, now);
}
