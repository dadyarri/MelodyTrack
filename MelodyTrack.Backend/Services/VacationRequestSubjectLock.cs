using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IVacationRequestSubjectLock
{
    Task AcquireAsync(VacationRequestSubjectType subjectType, Ulid subjectId, CancellationToken ct);
    Task AcquireWorkingHoursAsync(Ulid subjectUserId, CancellationToken ct);
}

public sealed class VacationRequestSubjectLock(AppDbContext db) : IVacationRequestSubjectLock
{
    public async Task AcquireAsync(VacationRequestSubjectType subjectType, Ulid subjectId, CancellationToken ct)
    {
        await AcquireSchedulingAsync(ct);
    }

    public async Task AcquireWorkingHoursAsync(Ulid subjectUserId, CancellationToken ct)
    {
        await AcquireSchedulingAsync(ct);
    }

    public static Task AcquireSchedulingAsync(AppDbContext context, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Scheduling mutations require a transaction.");
        }
        return context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41051)", ct);
    }

    private Task AcquireSchedulingAsync(CancellationToken ct) => AcquireSchedulingAsync(db, ct);
}
