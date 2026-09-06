using MelodyTrack.Backend.Api.AliasDictionaries.Requests;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.AliasDictionaries.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/alias-dictionaries/{dictionary}/{id}")]
public sealed class DeleteAliasDictionaryEntryEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] AliasDictionaryEntryRouteRequest request,
        AppDbContext db,
        IAuditLogService auditLogService,
        CancellationToken ct)
    {
        var dictionary = AliasDictionaryKindParser.Parse(request.Dictionary);
        if (await AliasDictionaryStore.DeleteAsync(db, dictionary, request.Id, ct))
        {
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.AliasDictionaryEntryDeleted,
                EntityType = $"alias_dictionary_{dictionary}",
                EntityId = request.Id.ToString()
            }, ct);
        }
        return TypedResults.NoContent();
    }
}
