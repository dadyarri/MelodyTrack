using FastEndpoints;
using MelodyTrack.Backend.Api.Reports.Reporting;
using MelodyTrack.Backend.Api.Reports.Requests;
using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Reports.Endpoints;

public sealed class GetWorkReportEndpoint(
    ICurrentUserAccessor currentUserAccessor,
    IReportContextFactory contextFactory,
    IWorkReportQueryService reportService)
    : Ep.Req<GetReportRequest>.Res<Results<Ok<WorkReportResponse>, UnauthorizedHttpResult, ForbidHttpResult, ApiProblemDetails>>
{
    public override void Configure()
    {
        Get("/reports/work");
        Options(builder => builder.RequireRateLimiting("expensive-read"));
    }

    public override async Task<Results<Ok<WorkReportResponse>, UnauthorizedHttpResult, ForbidHttpResult, ApiProblemDetails>> ExecuteAsync(
        GetReportRequest req,
        CancellationToken ct)
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
            AddError(result.Field!, result.Error!);
            return new ApiProblemDetails(ValidationFailures);
        }

        return TypedResults.Ok(await reportService.GetAsync(result.Context!, ct));
    }
}
