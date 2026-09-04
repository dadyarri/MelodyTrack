using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IWorkingHoursRequestQueryService
{
    Task<GetWorkingHoursRequestsResponse> GetMineAsync(User currentUser, CancellationToken ct);
    Task<GetWorkingHoursRequestsResponse> GetForReviewAsync(bool pendingOnly, CancellationToken ct);
    Task<WorkingHoursRequestResponse?> GetAccessibleAsync(Ulid requestId, User currentUser, CancellationToken ct);
}

public sealed class WorkingHoursRequestQueryService(AppDbContext db) : IWorkingHoursRequestQueryService
{
    public async Task<GetWorkingHoursRequestsResponse> GetMineAsync(User currentUser, CancellationToken ct)
    {
        var requests = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .Where(item => item.RequesterUserId == currentUser.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(250)
            .ToListAsync(ct);
        return new GetWorkingHoursRequestsResponse { Items = await MapAsync(requests, ct) };
    }

    public async Task<GetWorkingHoursRequestsResponse> GetForReviewAsync(bool pendingOnly, CancellationToken ct)
    {
        var query = db.WorkingHoursRequests.AsNoTracking().Include(item => item.RequestedWorkingHours).AsQueryable();
        query = pendingOnly
            ? query.Where(item => item.Status == VacationRequestStatus.Pending)
            : query.Where(item => item.Status != VacationRequestStatus.Pending);
        var requests = pendingOnly
            ? await query.OrderBy(item => item.CreatedAtUtc).Take(250).ToListAsync(ct)
            : await query.OrderByDescending(item => item.CreatedAtUtc).Take(250).ToListAsync(ct);
        return new GetWorkingHoursRequestsResponse { Items = await MapAsync(requests, ct) };
    }

    public async Task<WorkingHoursRequestResponse?> GetAccessibleAsync(Ulid requestId, User currentUser, CancellationToken ct)
    {
        var request = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null || !currentUser.Role.RoleName.IsSuperuser() && request.RequesterUserId != currentUser.Id)
        {
            return null;
        }

        return (await MapAsync([request], ct)).Single();
    }

    private async Task<IReadOnlyList<WorkingHoursRequestResponse>> MapAsync(
        IReadOnlyList<WorkingHoursRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var userIds = requests
            .SelectMany(item => new[] { item.RequesterUserId, item.SubjectUserId })
            .Distinct()
            .ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(item => userIds.Contains(item.Id))
            .Select(item => new PersonInfo(item.Id, item.FirstName, item.LastName, item.Role.DisplayName))
            .ToDictionaryAsync(item => item.Id, ct);
        var currentHours = await db.UserWorkingHoursDays.AsNoTracking()
            .Where(item => userIds.Contains(item.UserId))
            .OrderBy(item => item.DayOfWeek)
            .ToListAsync(ct);

        return requests.Select(request =>
        {
            var requester = users.GetValueOrDefault(request.RequesterUserId);
            var subject = users.GetValueOrDefault(request.SubjectUserId);
            var subjectHours = currentHours.Where(item => item.UserId == request.SubjectUserId).ToArray();
            var effectiveCurrentHours = subjectHours.Length > 0
                ? subjectHours.Select(MapDay).ToArray()
                : UserAvailabilityService.GetDefaultWorkingHours().Select(day => new WorkingHoursRequestDayResponse
                {
                    DayOfWeek = ToApiKey(day.DayOfWeek),
                    IsWorkingDay = day.IsWorkingDay,
                    StartTime = day.IsWorkingDay ? FormatTime(day.StartMinuteOfDay) : null,
                    EndTime = day.IsWorkingDay ? FormatTime(day.EndMinuteOfDay) : null
                }).ToArray();

            return new WorkingHoursRequestResponse
            {
                Id = request.Id,
                RequesterUserId = request.RequesterUserId,
                RequesterName = FormatName(requester),
                SubjectUserId = request.SubjectUserId,
                SubjectName = FormatName(subject),
                SubjectClassification = subject?.DisplayName ?? "Сотрудник",
                Status = ToApiKey(request.Status),
                RequestMessage = request.RequestMessage,
                CreatedAtUtc = request.CreatedAtUtc,
                ProcessedAtUtc = request.ProcessedAtUtc,
                ProcessedBySuperuserId = request.ProcessedBySuperuserId,
                DecisionMessage = request.DecisionMessage,
                Version = request.Version,
                RequestedWorkingHours = request.RequestedWorkingHours.OrderBy(item => item.DayOfWeek).Select(MapDay).ToArray(),
                CurrentWorkingHours = effectiveCurrentHours
            };
        }).ToArray();
    }

    private static WorkingHoursRequestDayResponse MapDay(WorkingHoursRequestDay day) => new()
    {
        DayOfWeek = ToApiKey(day.DayOfWeek),
        IsWorkingDay = day.IsWorkingDay,
        StartTime = day.IsWorkingDay ? FormatTime(day.StartMinuteOfDay) : null,
        EndTime = day.IsWorkingDay ? FormatTime(day.EndMinuteOfDay) : null
    };

    private static WorkingHoursRequestDayResponse MapDay(UserWorkingHoursDay day) => new()
    {
        DayOfWeek = ToApiKey(day.DayOfWeek),
        IsWorkingDay = day.IsWorkingDay,
        StartTime = day.IsWorkingDay ? FormatTime(day.StartMinuteOfDay) : null,
        EndTime = day.IsWorkingDay ? FormatTime(day.EndMinuteOfDay) : null
    };

    private static string FormatName(PersonInfo? value) => value is null
        ? "Недоступный сотрудник"
        : $"{value.LastName} {value.FirstName}".Trim();

    private static string ToApiKey(DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => "monday",
        DayOfWeek.Tuesday => "tuesday",
        DayOfWeek.Wednesday => "wednesday",
        DayOfWeek.Thursday => "thursday",
        DayOfWeek.Friday => "friday",
        DayOfWeek.Saturday => "saturday",
        _ => "sunday"
    };

    private static string ToApiKey(VacationRequestStatus value) => value switch
    {
        VacationRequestStatus.Approved => "approved",
        VacationRequestStatus.Declined => "declined",
        VacationRequestStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    private static string FormatTime(int totalMinutes) => $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";

    private sealed record PersonInfo(Ulid Id, string FirstName, string LastName, string DisplayName);
}
