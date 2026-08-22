using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using Facet.Mapping;
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

[ApiEndpoint(ApiMethod.Get, "/clients")]
public sealed class GetClientsEndpoint
{

    public static async Task<Results<Ok<PaginatedResponse<ClientWithBalanceDto>>, UnauthorizedHttpResult, ForbidHttpResult>>
        HandleAsync(
        [AsParameters] GetClientsPaginatedRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ClientToClientWithBalanceDtoMapConfig mapper,
        IRecordActivityService recordActivityService,
        TimeProvider timeProvider,
        ILogger<GetClientsEndpoint> logger,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug(
            "Fetching paginated list of clients with filters - Page: {Page}, PageSize: {PageSize}, FirstName: {FirstName}, LastName: {LastName}, Search: {Search}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified", req.Search ?? "not specified");

        var clientsQuery = db.Clients
            .AsNoTracking()
            .ApplyFuzzySearchFilters(req)
            .ApplyClientFullNameSearch(req.Search);

        if (req.LifecycleStatus is not null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
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
                                && appointment.Service.IsConsultation))
                        || client.Appointments.Any(appointment => !appointment.IsDeleted
                            && !appointment.Service.IsConsultation
                            && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned)
                            && client.Appointments.Any(consultation => !consultation.IsDeleted
                                && consultation.Status == AppointmentStatus.Completed
                                && consultation.Service.IsConsultation
                                && consultation.StartDate < appointment.StartDate)))),
                ClientLifecycleStatus.ThinkingLead => clientsQuery.Where(client => !client.IsLeadClosed
                    && !client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Planned
                        && appointment.StartDate >= now
                        && !appointment.Service.IsConsultation)
                    && client.Appointments.Any(appointment => !appointment.IsDeleted
                        && appointment.Status == AppointmentStatus.Completed
                        && appointment.Service.IsConsultation)
                    && !client.Appointments.Any(appointment => !appointment.IsDeleted
                        && !appointment.Service.IsConsultation
                        && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned)
                        && client.Appointments.Any(consultation => !consultation.IsDeleted
                            && consultation.Status == AppointmentStatus.Completed
                            && consultation.Service.IsConsultation
                            && consultation.StartDate < appointment.StartDate))),
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

        logger.LogInformation(
            "Retrieved {Count} clients (Page {Page} of {TotalPages}, Total: {TotalCount})",
            clients.Count,
            req.EffectivePage,
            (int)Math.Ceiling(totalCount / (double)req.EffectivePageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(clientsFacets, totalCount, req));
    }
}
