using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.ClientPortal.Aliases.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/client-portal/alias-candidates/next")]
public sealed class GetPortalAliasCandidateEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.ClientPortal)]
    public static async Task<Results<Ok<PortalAliasCandidateResponse>, Conflict<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetPortalAliasCandidateRequest request,
        PortalAliasGenerator generator,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.Role.RoleName.IsClient())
        {
            return TypedResults.Forbid();
        }

        if (user.PortalAlias is not null)
        {
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext, StatusCodes.Status409Conflict, "Псевдоним уже выбран и не может быть изменён."));
        }

        var candidate = await generator.GenerateAsync(user.Id, request, ct);
        return candidate is null
            ? TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext, StatusCodes.Status409Conflict, "Сейчас нет доступных вариантов имени. Обратитесь к администратору."))
            : TypedResults.Ok(candidate);
    }
}
