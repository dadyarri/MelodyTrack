using FastEndpoints;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class LookupRecurrenceTypesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<LookupRecurrenceTypesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/appointments/recurrenceTypes");
    }

    public override async Task<Results<Ok<LookupRecurrenceTypesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
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
            .ToListAsync(ct);

        return TypedResults.Ok(new LookupRecurrenceTypesResponse
        {
            RecurrenceTypes = recurrenceTypes
        });
    }
}
