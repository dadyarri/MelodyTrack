using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record ClientNotesExpectedState(string? Notes);
public sealed record ClientNotesPutRequest(Guid CommandId, string? Notes, ClientNotesExpectedState? ExpectedState);
public sealed record ClientNotesHistoryResponse(IReadOnlyList<ClientNotesHistoryEntry> Items);

public static class ClientNotesEndpoints
{
    // These claims describe only identity and must be supplied by verified authentication middleware.
    public const string ActorKindClaim = StaffIdentity.ActorKindClaim;

    public static IEndpointRouteBuilder MapClientNotesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/clients/{clientId:guid}/notes", PutAsync)
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientNotesWrite))
            .WithName("ChangeClientNotes")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        endpoints.MapGet("/api/clients/{clientId:guid}/notes", ReadCurrentAsync)
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientNotesRead))
            .WithName("GetClientNotes").Produces<ClientNotesCurrentResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        endpoints.MapGet("/api/clients/{clientId:guid}/notes/history", ReadHistoryAsync)
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientNotesRead))
            .WithName("GetClientNotesHistory").Produces<ClientNotesHistoryResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    public static async Task<Results<Ok<ClientNotesCurrentResponse>, ProblemHttpResult>> ReadCurrentAsync(Guid clientId,
        HttpContext context, CurrentStaff current, ClientNotesService notes, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await notes.ReadCurrentAsync(new(clientId), current.Actor, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new ClientNotesCurrentResponse(result.Value))
            : FailureOk<ClientNotesCurrentResponse>(MapError(result.Error));
    }

    public static async Task<Results<Ok<ClientNotesHistoryResponse>, ProblemHttpResult>> ReadHistoryAsync(Guid clientId,
        HttpContext context, CurrentStaff current, ClientNotesService notes, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await notes.ReadHistoryAsync(new(clientId), current.Actor, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new ClientNotesHistoryResponse(result.Value))
            : FailureOk<ClientNotesHistoryResponse>(MapError(result.Error));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> PutAsync(Guid clientId, ClientNotesPutRequest request,
        CurrentStaff current, ClientNotesService notes, TimeProvider clock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clientId == Guid.Empty || request.CommandId == Guid.Empty || request.ExpectedState is null)
            return Failure(new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "request")));
        var result = await notes.ChangeExpectedAsync(new(clientId), request.CommandId, request.ExpectedState.Notes,
            request.Notes, clock.GetUtcNow(), current.Actor, cancellationToken);
        if (result.IsSuccess) return UnitResult.Success<OperationError>().ToHttpResult(MapIssue);
        return Failure(MapError(result.Error));
    }

    private static OperationError MapError(ErrorList error)
    {
        if (error.Issues.Any(issue => issue.Error is ModelError.Forbidden)) return new OperationError.Forbidden();
        if (error.Issues.Any(issue => issue.Error is ModelError.NotFound)) return new OperationError.NotFound();
        if (error.Issues.Any(issue => issue.Error is ModelError.InvalidState or ModelError.Duplicate)) return new OperationError.Conflict(error);
        return new OperationError.Validation(error);
    }

    // Stable category/field metadata only: neither notes nor expected text enter Problem Details.
    private static ApiIssue MapIssue(DomainIssue issue) => new(issue.Error.Code, "Операция с заметками не выполнена", issue.Field);

    private static Results<NoContent, ProblemHttpResult> Failure(OperationError error) =>
        UnitResult.Failure<OperationError>(error).ToHttpResult(MapIssue);

    private static Results<Ok<T>, ProblemHttpResult> FailureOk<T>(OperationError error) =>
        Result.Failure<T, OperationError>(error).ToHttpResult(TypedResults.Ok, MapIssue);
}
