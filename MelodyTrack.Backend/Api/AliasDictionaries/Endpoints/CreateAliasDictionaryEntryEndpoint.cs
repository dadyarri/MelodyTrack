using MelodyTrack.Backend.Api.AliasDictionaries.Requests;
using MelodyTrack.Backend.Api.AliasDictionaries.Responses;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.AliasDictionaries.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/alias-dictionaries/{dictionary}")]
public sealed class CreateAliasDictionaryEntryEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public static async Task<Results<Created<AliasDictionaryEntryDto>, Conflict<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [FromRoute] string dictionary,
        CreateAliasDictionaryEntryRequest request,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var dictionaryKind = AliasDictionaryKindParser.Parse(dictionary);
        var value = request.Value.Trim();
        try
        {
            var entry = await AliasDictionaryStore.AddAsync(db, dictionaryKind, value, ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.AliasDictionaryEntryCreated,
                EntityType = $"alias_dictionary_{dictionaryKind}",
                EntityId = entry.Id.ToString(),
                Details = AuditDetailsFormatter.DescribeContext("Значение", value)
            }, ct);
            return TypedResults.Created($"/alias-dictionaries/{dictionary}/{entry.Id}", entry);
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
