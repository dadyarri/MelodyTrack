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
        var lockKey = $"vacation-request:{subjectType}:{subjectId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            ct);
    }

    public async Task AcquireWorkingHoursAsync(Ulid subjectUserId, CancellationToken ct)
    {
        var lockKey = $"working-hours-request:{subjectUserId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            ct);
    }
}
