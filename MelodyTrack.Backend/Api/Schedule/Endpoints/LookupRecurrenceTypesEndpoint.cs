using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/appointment-recurrence-types/options")]
public static class LookupRecurrenceTypesEndpoint
{
    [Authorize]
    public static async Task<Ok<LookupRecurrenceTypesResponse>> HandleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var recurrenceTypes = await db.RecurrenceTypes
            .AsNoTracking()
            .OrderBy(e => e.Type)
            .Select(e => new LookupRecurrenceTypeDto
            {
                Id = e.Id,
                DisplayName = e.DisplayName,
                Key = e.Type == AppointmentRecurrenceType.Daily
                    ? "daily"
                    : e.Type == AppointmentRecurrenceType.Weekly
                        ? "weekly"
                        : "monthly"
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new LookupRecurrenceTypesResponse
        {
            RecurrenceTypes = recurrenceTypes
        });
    }
}
