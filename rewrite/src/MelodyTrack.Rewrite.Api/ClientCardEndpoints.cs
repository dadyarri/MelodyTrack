using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record ClientCardContactRequest(Guid Id, Guid KindId, string Value, string? Comment, bool IsPrimary);
public sealed record ClientCardValuesRequest(string? FirstName, string? LastName, string? MiddleName,
    DateOnly? Birthdate, Guid? SourceId, IReadOnlyList<ClientCardContactRequest>? Contacts);
public sealed record ClientCardCreateRequest(Guid CommandId, Guid ClientId, string? FirstName, string? LastName,
    string? MiddleName, DateOnly? Birthdate, Guid? SourceId, IReadOnlyList<ClientCardContactRequest>? Contacts);
public sealed record ClientCardUpdateRequest(Guid CommandId, ClientCardValuesRequest? ExpectedState,
    string? FirstName, string? LastName, string? MiddleName, DateOnly? Birthdate, Guid? SourceId,
    IReadOnlyList<ClientCardContactRequest>? Contacts);
public sealed record ClientCardCreateResponse(ClientCard Client, bool HasDuplicateContacts);
public sealed record ClientCardUpdateResponse(bool HasDuplicateContacts);
public sealed record ClientArchiveRequest(Guid CommandId);
public sealed record ClientNotesCurrentResponse(string? Notes);

public static class ClientCardEndpoints
{
    public static IEndpointRouteBuilder MapClientCardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clients");
        group.MapGet("/{clientId:guid}", ReadAsync).RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsRead))
            .WithName("GetClientCard").ProducesProblem(404);
        group.MapPost("", CreateAsync).RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsWrite))
            .WithName("CreateClientCard").Produces<ClientCardCreateResponse>(201).ProducesProblem(409);
        group.MapPut("/{clientId:guid}", UpdateAsync).RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsWrite))
            .WithName("UpdateClientCard").Produces<ClientCardUpdateResponse>().ProducesProblem(409);
        group.MapPost("/{clientId:guid}/restore", RestoreAsync).RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsWrite))
            .WithName("RestoreClientCard").Produces(StatusCodes.Status204NoContent).ProducesProblem(409);
        group.MapPost("/{clientId:guid}/archive", ArchiveAsync).RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ClientsWrite))
            .WithName("ArchiveClientCard").ProducesProblem(409);
        return endpoints;
    }

    public static async Task<Results<Ok<ClientCard>, ProblemHttpResult>> ReadAsync(Guid clientId, HttpContext context,
        CurrentStaff current, ClientCardsService cards, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await cards.ReadAsync(new(clientId), current.Actor, cancellationToken);
        return result.ToHttpResult(TypedResults.Ok, MapIssue);
    }

    public static async Task<Results<Created<ClientCardCreateResponse>, ProblemHttpResult>> CreateAsync(
        ClientCardCreateRequest request, HttpContext context, CurrentStaff current, ClientCardsService cards,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryValues(request.FirstName, request.LastName, request.MiddleName, request.Birthdate, request.SourceId, request.Contacts, out var values))
            return FailureCreated<ClientCardCreateResponse>($"/api/clients/{request.ClientId:D}", Invalid("values"));
        var result = await cards.CreateAsync(request.CommandId, current.Actor is Actor.Staff staff ? staff.Id : default,
            new(request.ClientId), values, current.Actor, clock.GetUtcNow(), cancellationToken);
        return result.ToHttpResult(value => TypedResults.Created($"/api/clients/{request.ClientId:D}",
            new ClientCardCreateResponse(value.Client!, value.HasDuplicateContacts)), MapIssue);
    }

    public static async Task<Results<Ok<ClientCardUpdateResponse>, ProblemHttpResult>> UpdateAsync(Guid clientId,
        ClientCardUpdateRequest request, HttpContext context, CurrentStaff current, ClientCardsService cards,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request.ExpectedState is null || !TryValues(request.FirstName, request.LastName, request.MiddleName, request.Birthdate, request.SourceId, request.Contacts, out var values)
            || !TryValues(request.ExpectedState, out var expected, requireSource: false)) return Failure<ClientCardUpdateResponse>(Invalid("values"));
        var result = await cards.UpdateAsync(request.CommandId, current.Actor is Actor.Staff staff ? staff.Id : default,
            new(clientId), new(expected), values, current.Actor, clock.GetUtcNow(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new ClientCardUpdateResponse(result.Value.HasDuplicateContacts))
            : Failure<ClientCardUpdateResponse>(result.Error);
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> RestoreAsync(Guid clientId, ClientArchiveRequest request,
        CurrentStaff current, ClientCardsService cards, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await cards.RestoreAsync(request.CommandId, current.Actor is Actor.Staff staff ? staff.Id : default,
            new(clientId), current.Actor, clock.GetUtcNow(), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Failure(result.Error);
    }

    public static Results<NoContent, ProblemHttpResult> ArchiveAsync(Guid clientId, ClientArchiveRequest request,
        CurrentStaff current, ClientCardsService cards, TimeProvider clock) => Failure(cards.Archive(current.Actor, clock.GetUtcNow()).Error);

    private static bool TryValues(ClientCardValuesRequest request, out ClientCardValues values, bool requireSource = true)
        => TryValues(request.FirstName, request.LastName, request.MiddleName, request.Birthdate, request.SourceId, request.Contacts, out values, requireSource);

    private static bool TryValues(string? firstName, string? lastName, string? middleName, DateOnly? birthdate,
        Guid? sourceId, IReadOnlyList<ClientCardContactRequest>? contacts, out ClientCardValues values, bool requireSource = true)
    {
        values = null!;
        if (firstName is null || lastName is null || contacts is null || requireSource && sourceId is null
            || contacts.Any(item => item is null || item.Value is null)) return false;
        values = new(firstName, lastName, middleName, birthdate, sourceId,
            contacts.Select(item => new ClientContactInput(item.Id, item.KindId, item.Value, item.Comment, item.IsPrimary)).ToArray());
        return true;
    }

    private static OperationError Invalid(string field) => new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), field));
    private static ApiIssue MapIssue(DomainIssue issue) => new(issue.Error.Code, "Операция с карточкой клиента не выполнена", issue.Field);
    private static Results<Ok<T>, ProblemHttpResult> Failure<T>(OperationError error) => Result.Failure<T, OperationError>(error)
        .ToHttpResult(TypedResults.Ok, MapIssue);
    private static Results<Created<T>, ProblemHttpResult> FailureCreated<T>(string location, OperationError error) =>
        Result.Failure<T, OperationError>(error).ToHttpResult(value => TypedResults.Created(location, value), MapIssue);
    private static Results<NoContent, ProblemHttpResult> Failure(OperationError error) => UnitResult.Failure<OperationError>(error).ToHttpResult(MapIssue);
}
