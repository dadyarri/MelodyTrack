using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Vacations.Requests;
using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Vacations.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/vacations")]
public sealed class CreateVacationEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<VacationOperationResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        CreateVacationInput input,
        VacationCommandService commands,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var actor = await currentUserAccessor.GetAsync(ct);
        if (actor is null) return TypedResults.Unauthorized();
        var result = await commands.CreateAsync(actor, input, ct);
        if (result.Failure == VacationRequestWorkflowFailure.Forbidden) return TypedResults.Forbid();
        if (result.Failure == VacationRequestWorkflowFailure.NotFound)
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound, result.Detail));
        if (result.Failure == VacationRequestWorkflowFailure.Conflict)
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status409Conflict, result.Detail));
        return TypedResults.Ok(result.Response!);
    }
}
