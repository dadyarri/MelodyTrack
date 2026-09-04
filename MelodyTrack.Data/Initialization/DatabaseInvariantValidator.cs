using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Data.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Data.Initialization;

public static class DatabaseInvariantValidator
{
    private static readonly UserRoles[] RequiredRoles =
    [
        UserRoles.Superuser,
        UserRoles.Admin,
        UserRoles.User
    ];

    private static readonly AppointmentRecurrenceType[] RequiredRecurrenceTypes =
    [
        AppointmentRecurrenceType.Daily,
        AppointmentRecurrenceType.Weekly,
        AppointmentRecurrenceType.Monthly
    ];

    public static async Task ValidateAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existingRoles = await db.Roles
            .AsNoTracking()
            .Select(e => e.RoleName)
            .ToListAsync(ct);

        foreach (var requiredRole in RequiredRoles)
        {
            if (!existingRoles.Contains(requiredRole))
            {
                throw new MissingReferenceDataInDatabaseException(
                    nameof(UserRoles),
                    requiredRole.ToString());
            }
        }

        var existingRecurrenceTypes = await db.RecurrenceTypes
            .AsNoTracking()
            .Select(e => e.Type)
            .ToListAsync(ct);

        foreach (var requiredRecurrenceType in RequiredRecurrenceTypes)
        {
            if (!existingRecurrenceTypes.Contains(requiredRecurrenceType))
            {
                throw new MissingReferenceDataInDatabaseException(
                    nameof(AppointmentRecurrenceType),
                    requiredRecurrenceType.ToString());
            }
        }
    }
}
