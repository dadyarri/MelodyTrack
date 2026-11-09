using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record ExpensePostRequest(Guid CommandId, string? Description,
    [property: JsonRequired] decimal Amount, [property: JsonRequired] DateOnly Date, Guid? CategoryId);
public sealed record ExpenseExpectedState(string? Description,
    [property: JsonRequired] decimal Amount, [property: JsonRequired] DateOnly Date, Guid? CategoryId);
public sealed record ExpensePutRequest(Guid CommandId, string? Description,
    [property: JsonRequired] decimal Amount, [property: JsonRequired] DateOnly Date,
    Guid? CategoryId, string? Comment, ExpenseExpectedState? ExpectedState);
public sealed record ExpenseResponse(Guid Id);

public static class ExpensesEndpoints
{
    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Describe(endpoints.MapPost("/api/expenses", PostAsync).WithName("CreateExpense")
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ExpensesRecord)));
        Describe(endpoints.MapPut("/api/expenses/{expenseId:guid}", PutAsync).WithName("CorrectExpense")
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.ExpensesCorrect)));
        return endpoints;
    }

    private static void Describe(RouteHandlerBuilder endpoint) => endpoint
        .Produces<ExpenseResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<ExpenseResponse>, ProblemHttpResult>> PostAsync(ExpensePostRequest request,
        CurrentStaff current, ExpenseService expenses, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await expenses.CreateAsync(request.CommandId,
            new(request.Description, request.Amount, request.Date, request.CategoryId), current.Actor, clock.GetUtcNow(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new ExpenseResponse(result.Value.After!.Id.Value)) : Failure(MapError(result.Error));
    }

    public static async Task<Results<Ok<ExpenseResponse>, ProblemHttpResult>> PutAsync(Guid expenseId, ExpensePutRequest request,
        CurrentStaff current, ExpenseService expenses, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.ExpectedState is null)
            return Failure(new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "expectedState")));
        var expected = request.ExpectedState;
        var result = await expenses.EditAsync(new(expenseId), request.CommandId,
            new(expected.Description, expected.Amount, expected.Date, expected.CategoryId),
            new(request.Description, request.Amount, request.Date, request.CategoryId), request.Comment,
            current.Actor, clock.GetUtcNow(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new ExpenseResponse(result.Value.After!.Id.Value)) : Failure(MapError(result.Error));
    }

    private static OperationError MapError(ErrorList error)
    {
        if (error.Issues.Any(issue => issue.Error is ModelError.Forbidden)) return new OperationError.Forbidden();
        if (error.Issues.Any(issue => issue.Error is ModelError.NotFound)) return new OperationError.NotFound();
        if (error.Issues.Any(issue => issue.Error is ModelError.InvalidState or ModelError.Duplicate)) return new OperationError.Conflict(error);
        return new OperationError.Validation(error);
    }

    private static Results<Ok<ExpenseResponse>, ProblemHttpResult> Failure(OperationError error) =>
        Result.Failure<ExpenseResponse, OperationError>(error).ToHttpResult(TypedResults.Ok,
            issue => new ApiIssue(issue.Error.Code, "Операция с расходом не выполнена", issue.Field));
}
