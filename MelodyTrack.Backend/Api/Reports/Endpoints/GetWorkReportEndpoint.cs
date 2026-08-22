using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Reports.Reporting;
using MelodyTrack.Backend.Api.Reports.Requests;
using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Reports.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/reports/work")]
public sealed class GetWorkReportEndpoint
{

        [EnableRateLimiting("expensive-read")]
    public static async Task<Results<Ok<WorkReportResponse>, UnauthorizedHttpResult, ForbidHttpResult, ApiProblemDetails>> HandleAsync(
        [AsParameters] GetReportRequest req,
        ICurrentUserAccessor currentUserAccessor,
        IReportContextFactory contextFactory,
        IWorkReportQueryService reportService,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var result = contextFactory.Create(req, currentUser);
        if (!result.IsSuccess)
        {
            validationErrors.Add(result.Field!, result.Error!);
            return new ApiProblemDetails(validationErrors);
        }

        return TypedResults.Ok(await reportService.GetAsync(result.Context!, ct));
    }
}
