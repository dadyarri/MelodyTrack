using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetDashboardStatsEndpoint(
    IPersonalDashboardQueryService dashboardQueryService,
    ICurrentUserAccessor currentUserAccessor,
    TimeProvider timeProvider)
    : Ep.Req<GetDashboardStatsRequest>.Res<Results<Ok<GetDashboardStatsResponse>, UnauthorizedHttpResult, ApiProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard");
        Options(builder => builder.RequireRateLimiting("expensive-read"));
    }

    public override async Task<Results<Ok<GetDashboardStatsResponse>, UnauthorizedHttpResult, ApiProblemDetails>> ExecuteAsync(
        GetDashboardStatsRequest req,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            AddError(r => r.Timezone, "Часовой пояс не найден");
            return new ApiProblemDetails(ValidationFailures);
        }
        catch (InvalidTimeZoneException)
        {
            AddError(r => r.Timezone, "Часовой пояс недоступен");
            return new ApiProblemDetails(ValidationFailures);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return TypedResults.Ok(await dashboardQueryService.GetAsync(
            currentUser.Id,
            timezone,
            nowUtc,
            DashboardAccess.CanViewDashboardAnalytics(currentUser),
            ct));
    }
}
