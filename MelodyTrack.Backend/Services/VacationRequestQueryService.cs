using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IVacationRequestQueryService
{
    Task<GetVacationRequestsResponse> GetMineAsync(User currentUser, CancellationToken ct);
    Task<GetVacationRequestsResponse> GetForReviewAsync(bool pendingOnly, CancellationToken ct);
    Task<VacationRequestResponse?> GetAccessibleAsync(Ulid requestId, User currentUser, CancellationToken ct);
}

public sealed class VacationRequestQueryService(AppDbContext db) : IVacationRequestQueryService
{
    public async Task<GetVacationRequestsResponse> GetMineAsync(User currentUser, CancellationToken ct)
    {
        var requesterType = currentUser.Role.RoleName.IsClient()
            ? VacationRequestPrincipalType.Client
            : VacationRequestPrincipalType.Staff;
        var requesterId = requesterType == VacationRequestPrincipalType.Client
            ? currentUser.ClientId
            : currentUser.Id;
        if (requesterId is null)
        {
            return new GetVacationRequestsResponse { Items = [] };
        }

        var requests = await db.VacationRequests.AsNoTracking()
            .Where(item => item.RequesterPrincipalType == requesterType && item.RequesterId == requesterId.Value)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);
        return new GetVacationRequestsResponse { Items = await MapAsync(requests, ct) };
    }

    public async Task<GetVacationRequestsResponse> GetForReviewAsync(bool pendingOnly, CancellationToken ct)
    {
        var query = db.VacationRequests.AsNoTracking();
        query = pendingOnly
            ? query.Where(item => item.Status == VacationRequestStatus.Pending)
            : query.Where(item => item.Status != VacationRequestStatus.Pending);

        var requests = pendingOnly
            ? await query.OrderBy(item => item.CreatedAtUtc).Take(250).ToListAsync(ct)
            : await query.OrderByDescending(item => item.CreatedAtUtc).Take(250).ToListAsync(ct);
        return new GetVacationRequestsResponse { Items = await MapAsync(requests, ct) };
    }

    public async Task<VacationRequestResponse?> GetAccessibleAsync(Ulid requestId, User currentUser, CancellationToken ct)
    {
        var request = await db.VacationRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return null;
        }

        if (!currentUser.Role.RoleName.IsSuperuser())
        {
            var requesterType = currentUser.Role.RoleName.IsClient()
                ? VacationRequestPrincipalType.Client
                : VacationRequestPrincipalType.Staff;
            var requesterId = requesterType == VacationRequestPrincipalType.Client ? currentUser.ClientId : currentUser.Id;
            if (request.RequesterPrincipalType != requesterType || requesterId is null || request.RequesterId != requesterId.Value)
            {
                return null;
            }
        }

        return (await MapAsync([request], ct)).Single();
    }

    private async Task<IReadOnlyList<VacationRequestResponse>> MapAsync(
        IReadOnlyList<VacationRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var staffIds = requests
            .Where(item => item.SubjectType == VacationRequestSubjectType.Staff)
            .Select(item => item.SubjectId)
            .Concat(requests.Where(item => item.RequesterPrincipalType == VacationRequestPrincipalType.Staff).Select(item => item.RequesterId))
            .Distinct()
            .ToArray();
        var clientIds = requests
            .Where(item => item.SubjectType == VacationRequestSubjectType.Client)
            .Select(item => item.SubjectId)
            .Concat(requests.Where(item => item.RequesterPrincipalType == VacationRequestPrincipalType.Client).Select(item => item.RequesterId))
            .Distinct()
            .ToArray();

        var staff = await db.Users.AsNoTracking()
            .Where(item => staffIds.Contains(item.Id))
            .Select(item => new PersonInfo(item.Id, item.FirstName, item.LastName, item.Role.DisplayName))
            .ToDictionaryAsync(item => item.Id, ct);
        var clients = await db.Clients.AsNoTracking()
            .Where(item => clientIds.Contains(item.Id))
            .Select(item => new PersonInfo(item.Id, item.FirstName, item.LastName, "Клиент"))
            .ToDictionaryAsync(item => item.Id, ct);
        var staffVacations = await db.UserVacations.AsNoTracking()
            .Where(item => staffIds.Contains(item.UserId))
            .Select(item => new { SubjectId = item.UserId, item.StartDate, item.EndDate })
            .ToListAsync(ct);
        var clientVacations = await db.ClientVacations.AsNoTracking()
            .Where(item => clientIds.Contains(item.ClientId))
            .Select(item => new { SubjectId = item.ClientId, item.StartDate, item.EndDate })
            .ToListAsync(ct);

        var minStart = requests.Min(item => item.RequestedStart);
        var maxEnd = requests.Max(item => item.RequestedEnd);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.Status == AppointmentStatus.Planned &&
                item.StartDate < maxEnd &&
                item.EndDate > minStart &&
                (clientIds.Contains(item.Client.Id) || item.Provider != null && staffIds.Contains(item.Provider.Id)))
            .Select(item => new
            {
                ClientId = item.Client.Id,
                ProviderId = item.Provider == null ? (Ulid?)null : item.Provider.Id,
                item.StartDate,
                item.EndDate
            })
            .ToListAsync(ct);

        return requests.Select(request =>
        {
            var requesterName = request.RequesterPrincipalType == VacationRequestPrincipalType.Staff
                ? FormatStaffName(staff.GetValueOrDefault(request.RequesterId))
                : FormatClientName(clients.GetValueOrDefault(request.RequesterId));
            var subjectName = request.SubjectType == VacationRequestSubjectType.Staff
                ? FormatStaffName(staff.GetValueOrDefault(request.SubjectId))
                : FormatClientName(clients.GetValueOrDefault(request.SubjectId));
            var classification = request.SubjectType == VacationRequestSubjectType.Client
                ? "Клиент"
                : staff.GetValueOrDefault(request.SubjectId)?.DisplayName ?? "Сотрудник";
            var existingVacations = request.SubjectType == VacationRequestSubjectType.Staff
                ? staffVacations.Where(item => item.SubjectId == request.SubjectId)
                    .Select(item => new VacationPeriodResponse { StartDate = item.StartDate, EndDate = item.EndDate })
                    .ToArray()
                : clientVacations.Where(item => item.SubjectId == request.SubjectId)
                    .Select(item => new VacationPeriodResponse { StartDate = item.StartDate, EndDate = item.EndDate })
                    .ToArray();
            var conflictingAppointmentCount = appointments.Count(item =>
                item.StartDate < request.RequestedEnd &&
                item.EndDate > request.RequestedStart &&
                (request.SubjectType == VacationRequestSubjectType.Client
                    ? item.ClientId == request.SubjectId
                    : item.ProviderId == request.SubjectId));

            return new VacationRequestResponse
            {
                Id = request.Id,
                RequesterType = ToApiKey(request.RequesterPrincipalType),
                RequesterId = request.RequesterId,
                RequesterName = requesterName,
                SubjectType = ToApiKey(request.SubjectType),
                SubjectId = request.SubjectId,
                SubjectName = subjectName,
                SubjectClassification = classification,
                StartDate = request.RequestedStart,
                EndDate = request.RequestedEnd,
                Status = ToApiKey(request.Status),
                RequestMessage = request.RequestMessage,
                CreatedAtUtc = request.CreatedAtUtc,
                ProcessedAtUtc = request.ProcessedAtUtc,
                ProcessedBySuperuserId = request.ProcessedBySuperuserId,
                DecisionMessage = request.DecisionMessage,
                ResultingVacationId = request.ResultingVacationId,
                Version = request.Version,
                ExistingVacations = existingVacations,
                ConflictingAppointmentCount = conflictingAppointmentCount
            };
        }).ToArray();
    }

    private static string FormatStaffName(PersonInfo? value) => value is null
        ? "Недоступный сотрудник"
        : $"{value.LastName} {value.FirstName}".Trim();

    private static string FormatClientName(PersonInfo? value) => value is null
        ? "Недоступный клиент"
        : $"{value.LastName} {value.FirstName}".Trim();

    private static string ToApiKey(VacationRequestPrincipalType value) => value switch
    {
        VacationRequestPrincipalType.Client => "client",
        _ => "staff"
    };

    private static string ToApiKey(VacationRequestSubjectType value) => value switch
    {
        VacationRequestSubjectType.Client => "client",
        _ => "staff"
    };

    private static string ToApiKey(VacationRequestStatus value) => value switch
    {
        VacationRequestStatus.Approved => "approved",
        VacationRequestStatus.Declined => "declined",
        VacationRequestStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    private sealed record PersonInfo(Ulid Id, string FirstName, string LastName, string DisplayName);
}
