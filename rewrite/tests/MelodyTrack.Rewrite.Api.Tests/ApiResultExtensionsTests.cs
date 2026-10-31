using System.Text.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ApiResultExtensionsTests
{
    private sealed record ResponseDto(int Number);
    private static ApiIssue MapIssue(DomainIssue issue) => new(issue.Error.Code, "Проверьте значение", issue.Field is null ? null : "profile." + issue.Field);

    [Fact]
    public void ToHttpResult_Success_MapsValueToResponseDto()
    {
        var result = Result.Success<int, OperationError>(42);
        var response = result.ToHttpResult(value => TypedResults.Ok(new ResponseDto(value)), _ => throw new InvalidOperationException());
        Assert.Equal(42, Assert.IsType<Ok<ResponseDto>>(response.Result).Value!.Number);
    }

    [Fact]
    public void ToHttpResult_CreatedResponse_PreservesStatusAndLocation()
    {
        var result = Result.Success<int, OperationError>(42);
        var response = result.ToHttpResult(value => TypedResults.Created($"/api/clients/{value}", new ResponseDto(value)), MapIssue);
        var created = Assert.IsType<Created<ResponseDto>>(response.Result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal("/api/clients/42", created.Location);
    }

    [Fact]
    public void ToHttpResult_ValidationFailure_PreservesAllIssuesAndMapsFields()
    {
        var errors = new ErrorListBuilder()
            .Add(new CatalogueError.Required("firstName"), "firstName")
            .Add(new CatalogueError.Required("lastName"), "lastName")
            .ToResult().Error;
        var result = Result.Failure<int, OperationError>(new OperationError.Validation(errors));
        var response = result.ToHttpResult<int, Ok<int>>(_ => throw new InvalidOperationException(), MapIssue, "test-trace");
        var problem = Assert.IsType<ProblemHttpResult>(response.Result).ProblemDetails;
        Assert.Equal(400, problem.Status);
        Assert.Equal("test-trace", problem.Extensions["traceId"]);
        var issues = Assert.IsType<ApiIssue[]>(problem.Extensions["errors"]);
        Assert.Equal(new[] { "profile.firstName", "profile.lastName" }, issues.Select(issue => issue.Field));
        Assert.All(issues, issue => Assert.Equal("catalogue.required", issue.Code));
    }

    [Theory]
    [InlineData(false, 404)]
    [InlineData(true, 403)]
    public void ToHttpResult_AccessOrLookupFailure_UsesAppropriateStatus(bool forbidden, int status)
    {
        OperationError error = forbidden ? new OperationError.Forbidden() : new OperationError.NotFound();
        var result = Result.Failure<int, OperationError>(error);
        var response = result.ToHttpResult(value => TypedResults.Ok(value), MapIssue);
        var problem = Assert.IsType<ProblemHttpResult>(response.Result).ProblemDetails;
        Assert.Equal(status, problem.Status);
        Assert.False(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void ToHttpResult_StateConflict_UsesConflictStatus()
    {
        var result = Result.Failure<int, OperationError>(new OperationError.Conflict(ErrorList.Single(new ClientError.UnknownContact())));
        var response = result.ToHttpResult(value => TypedResults.Ok(value), MapIssue);
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(response.Result).StatusCode);
    }

    [Fact]
    public void ToHttpResult_UnitSuccess_ReturnsNoContent()
    {
        var response = UnitResult.Success<OperationError>().ToHttpResult(MapIssue);
        Assert.IsType<NoContent>(response.Result);
    }

    [Fact]
    public void ToHttpResult_UnitFailure_UsesSameErrorMapping()
    {
        var response = UnitResult.Failure<OperationError>(new OperationError.Validation(ErrorList.Single(new ClientError.InvalidContactValue(), "value"))).ToHttpResult(MapIssue);
        Assert.Equal(400, Assert.IsType<ProblemHttpResult>(response.Result).StatusCode);
    }

    [Fact]
    public async Task ToHttpResult_ValidationFailure_WritesProblemJsonWithoutResultEnvelope()
    {
        var result = Result.Failure<int, OperationError>(new OperationError.Validation(ErrorList.Single(new CatalogueError.Required("name"), "name")));
        var response = result.ToHttpResult(value => TypedResults.Ok(new ResponseDto(value)), MapIssue);
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        await using var stream = new MemoryStream();
        context.Response.Body = stream;

        await response.ExecuteAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("catalogue.required", json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("profile.name", json.RootElement.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.False(json.RootElement.TryGetProperty("isSuccess", out _));
        Assert.False(json.RootElement.TryGetProperty("value", out _));
    }
}
