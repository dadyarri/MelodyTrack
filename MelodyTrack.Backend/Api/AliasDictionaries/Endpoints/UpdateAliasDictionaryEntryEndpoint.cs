using MelodyTrack.Backend.Api.AliasDictionaries.Requests;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.AliasDictionaries.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/alias-dictionaries/{dictionary}/{id}")]
public sealed class UpdateAliasDictionaryEntryEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] AliasDictionaryEntryRouteRequest route,
        UpdateAliasDictionaryEntryRequest request,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var dictionary = AliasDictionaryKindParser.Parse(route.Dictionary);
            var updated = await AliasDictionaryStore.UpdateAsync(db, dictionary, route.Id, request.Value.Trim(), request.IsEnabled, ct);
            if (updated)
            {
                await auditLogService.WriteAsync(new AuditLogWriteRequest
                {
                    Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.AliasDictionaryEntryUpdated,
                    EntityType = $"alias_dictionary_{dictionary}",
                    EntityId = route.Id.ToString(),
                    Details = AuditDetailsFormatter.DescribeContext("Значение", request.Value.Trim())
                }, ct);
            }
            return updated
                ? TypedResults.NoContent()
                : TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound));
        }
        catch (Exception exception) when (AliasDictionaryErrors.IsUniqueViolation(exception))
        {
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status409Conflict,
                "Такое значение уже есть в словаре."));
        }
    }
}
