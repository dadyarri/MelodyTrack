using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Rewrite.Api;

public sealed class ClientDirectoryRequest
{
    [FromQuery(Name = "q")] public string? Q { get; init; }
    [FromQuery(Name = "archive")] public string? Archive { get; init; }
    [FromQuery(Name = "sourceId")] public Guid? SourceId { get; init; }
    [FromQuery(Name = "withoutSource")] public bool? WithoutSource { get; init; }
    [FromQuery(Name = "sort")] public string? Sort { get; init; }
    [FromQuery(Name = "page")] public int? Page { get; init; }
    [FromQuery(Name = "pageSize")] public int? PageSize { get; init; }
    public override string ToString() => nameof(ClientDirectoryRequest);
}

public static class ClientDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapClientDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clients")
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsRead));
        Describe(group.MapGet("", ReadAsync).WithName("GetClientDirectory"));
        Describe(group.MapGet("/options", OptionsAsync).WithName("GetClientDirectoryOptions"));
        return endpoints;
    }

    private static void Describe(RouteHandlerBuilder endpoint) => endpoint
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    public static async Task<Results<Ok<ClientDirectoryPage>, ProblemHttpResult>> ReadAsync(
        [AsParameters] ClientDirectoryRequest request, HttpContext context, CurrentStaff current,
        ClientDirectoryService directory, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = new ClientDirectoryQuery(request.Q, request.Archive ?? "active", request.SourceId,
            request.WithoutSource ?? false, request.Sort ?? "name:asc", request.Page ?? 1, request.PageSize ?? 10);
        var result = await directory.ReadAsync(query, current.Actor, cancellationToken);
        return result.ToHttpResult(TypedResults.Ok, MapIssue);
    }

    public static async Task<Results<Ok<ClientDirectoryOptions>, ProblemHttpResult>> OptionsAsync(
        HttpContext context, CurrentStaff current, ClientDirectoryService directory, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await directory.ReadOptionsAsync(current.Actor, cancellationToken);
        return result.ToHttpResult(TypedResults.Ok, MapIssue);
    }

    private static ApiIssue MapIssue(DomainIssue issue) => new(issue.Error.Code,
        "Проверьте параметры списка клиентов.", issue.Field);
}
