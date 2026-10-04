using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/dashboard")]
public sealed class GetDashboardStatsEndpoint
{

    [EnableRateLimiting("expensive-read")]
    public static async Task<Results<Ok<GetDashboardStatsResponse>, UnauthorizedHttpResult, ApiProblemDetails>> HandleAsync(
    [AsParameters] GetDashboardStatsRequest req,
    IPersonalDashboardQueryService dashboardQueryService,
    ICurrentUserAccessor currentUserAccessor,
    TimeProvider timeProvider,
    ApiValidationErrorCollection validationErrors,
    CancellationToken ct
)
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
            validationErrors.Add(nameof(req.Timezone), "Часовой пояс не найден");
            return new ApiProblemDetails(validationErrors);
        }
        catch (InvalidTimeZoneException)
        {
            validationErrors.Add(nameof(req.Timezone), "Часовой пояс недоступен");
            return new ApiProblemDetails(validationErrors);
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
