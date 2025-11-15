using System.Globalization;
using Facet.Extensions;
using FastEndpoints;
using Humanizer;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class GetMiniScheduleEndpoint(AppDbContext db) : Ep.Req<BaseGetAppointmentsRequest>.Res<Results<Ok<GetMiniScheduleResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/appointments/mini");
    }

    public override async Task<Results<Ok<GetMiniScheduleResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(BaseGetAppointmentsRequest req, CancellationToken ct)
    {
        var startOfToday = DateTime.Today;
        var endOfTomorrow = startOfToday.AddDays(2).AddTicks(-1);
        var cultureInfo = CultureInfo.GetCultureInfo("ru");

        var appointmentsDays = await db.Appointments
            .Where(e => e.StartDate >= startOfToday && e.StartDate <= endOfTomorrow)
            .Include(e => e.Client)
            .Include(e => e.Service)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Client.LastName)
            .ThenBy(e => e.Client.FirstName)
            .GroupBy(e => e.StartDate)
            .OrderBy(e => e.Key)
            .ToDictionaryAsync(
                e => e.Key.Humanize(culture: cultureInfo, dateToCompareAgainst: startOfToday),
                e => e.SelectFacets<Appointment, AppointmentDto>().ToList(),
                cancellationToken: ct
            );

        var result = new GetMiniScheduleResponse
        {
            Appointments = appointmentsDays
        };
        
        return TypedResults.Ok(result);
    }

}