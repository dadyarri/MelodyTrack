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

[ApiEndpoint(ApiMethod.Get, "/reports/finance")]
public sealed class GetFinanceReportEndpoint
{
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
        [EnableRateLimiting("expensive-read")]
    public static async Task<Results<Ok<FinanceReportResponse>, UnauthorizedHttpResult, ForbidHttpResult, ApiProblemDetails>> HandleAsync(
        [AsParameters] GetReportRequest req,
        ICurrentUserAccessor currentUserAccessor,
        IReportContextFactory contextFactory,
        IFinanceReportQueryService reportService,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct)
            ?? throw new InvalidOperationException("The administrator policy succeeded without a current user.");

        var result = contextFactory.Create(req, currentUser);
        if (!result.IsSuccess)
        {
            validationErrors.Add(result.Field!, result.Error!);
            return new ApiProblemDetails(validationErrors);
        }

        return TypedResults.Ok(await reportService.GetAsync(result.Context!, ct));
    }
}
