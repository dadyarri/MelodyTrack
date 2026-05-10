using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public enum AppointmentDeleteScope
{
    Single,
    ThisAndFollowing,
    All
}

public enum DeleteAppointmentResult
{
    Success,
    NotFound
}

public interface IAppointmentDeletionService
{
    Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct);
}

public class AppointmentDeletionService(AppDbContext db) : IAppointmentDeletionService
{
    public async Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstOrDefaultAsync(item => item.Id == appointmentId && !item.IsDeleted, ct);

        if (appointment is null)
        {
            return DeleteAppointmentResult.NotFound;
        }

        if (appointment.RecurringRule is null || scope == AppointmentDeleteScope.Single)
        {
            appointment.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return DeleteAppointmentResult.Success;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var recurringRuleId = appointment.RecurringRule.Id;

        if (scope == AppointmentDeleteScope.ThisAndFollowing)
        {
            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    item.StartDate >= appointment.StartDate &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);

            if (appointment.StartDate.Date <= appointment.RecurringRule.StartDate.Date)
            {
                db.RecurrenceRules.Remove(appointment.RecurringRule);
            }
            else
            {
                appointment.RecurringRule.EndDate = appointment.StartDate.Date.AddDays(-1);
            }
        }
        else
        {
            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);

            db.RecurrenceRules.Remove(appointment.RecurringRule);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return DeleteAppointmentResult.Success;
    }
}
