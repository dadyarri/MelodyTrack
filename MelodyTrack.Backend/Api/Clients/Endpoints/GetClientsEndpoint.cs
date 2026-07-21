using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetClientsPaginatedRequest>.Res<
        Results<Ok<PaginatedResponse<ClientWithBalanceDto>>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/clients");
    }

    public override async Task<Results<Ok<PaginatedResponse<ClientWithBalanceDto>>, UnauthorizedHttpResult, ForbidHttpResult>>
        ExecuteAsync(GetClientsPaginatedRequest req,
            CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        Logger.LogDebug(
            "Fetching paginated list of clients with filters - Page: {Page}, PageSize: {PageSize}, FirstName: {FirstName}, LastName: {LastName}, Search: {Search}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified", req.Search ?? "not specified");

        var clientsQuery = db.Clients
            .AsNoTracking()
            .ApplyFuzzySearchFilters(req)
            .ApplyClientFullNameSearch(req.Search);

        if (req.LifecycleStatus is not null)
        {
            var now = DateTime.UtcNow;
            clientsQuery = req.LifecycleStatus.Value switch
            {
                ClientLifecycleStatus.ClosedLead => clientsQuery.Where(client => client.IsLeadClosed),
                ClientLifecycleStatus.Client => clientsQuery.Where(client => !client.IsLeadClosed
                    && (client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Planned
                        && appointment.StartDate >= now
                        && !appointment.Service.IsConsultation)
                        || (!client.Appointments.Any(appointment => !appointment.IsDeleted
                            && appointment.Status == AppointmentStatus.Planned
                            && appointment.StartDate >= now
                            && !appointment.Service.IsConsultation)
                            && !client.Appointments.Any(appointment => !appointment.IsDeleted
                                && appointment.Status == AppointmentStatus.Completed
                                && appointment.Service.IsConsultation)
                            && !client.Appointments.Any(appointment => !appointment.IsDeleted
                                && appointment.Status == AppointmentStatus.Planned
                                && appointment.Service.IsConsultation)))),
                ClientLifecycleStatus.ThinkingLead => clientsQuery.Where(client => !client.IsLeadClosed
                    && !client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Planned
                        && appointment.StartDate >= now
                        && !appointment.Service.IsConsultation)
                    && client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Completed
                        && appointment.Service.IsConsultation)),
                ClientLifecycleStatus.Lead => clientsQuery.Where(client => !client.IsLeadClosed
                    && !client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Planned
                        && appointment.StartDate >= now
                        && !appointment.Service.IsConsultation)
                    && !client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Completed
                        && appointment.Service.IsConsultation)
                    && client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Planned
                        && appointment.Service.IsConsultation)),
                _ => clientsQuery
            };
        }

        var totalCount = await clientsQuery.CountAsync(ct);

        var clients = await clientsQuery
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ApplyPagination(req)
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .Include(e => e.Vacations)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);
        var clientActivities = await recordActivityService.GetLatestActivitiesAsync(
            "client",
            clientsFacets.Select(client => client.Id.ToString()).ToList(),
            ct);

        foreach (var client in clientsFacets)
        {
            if (clientActivities.TryGetValue(client.Id.ToString(), out var activity))
            {
                client.LastActivity = activity;
            }
        }

        Logger.LogInformation(
            "Retrieved {Count} clients (Page {Page} of {TotalPages}, Total: {TotalCount})",
            clients.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(clientsFacets, totalCount, req));
    }
}
