using MelodyTrack.Backend.Api.AliasDictionaries.Requests;
using MelodyTrack.Backend.Api.AliasDictionaries.Responses;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.AliasDictionaries.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/alias-dictionaries/{dictionary}")]
public sealed class GetAliasDictionaryEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<GetAliasDictionaryResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetAliasDictionaryRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var items = await AliasDictionaryStore.ListAsync(
            db, AliasDictionaryKindParser.Parse(request.Dictionary), request.Search, ct);
        return TypedResults.Ok(new GetAliasDictionaryResponse { Items = items });
    }
}
