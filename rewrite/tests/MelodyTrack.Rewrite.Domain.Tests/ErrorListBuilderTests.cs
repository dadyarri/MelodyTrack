using CSharpFunctionalExtensions;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ErrorListBuilderTests
{
    [Fact]
    public void ToResult_EmptyBuilder_ReturnsSuccess()
    {
        var result = new ErrorListBuilder().ToResult();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddIf_FalseCondition_DoesNotFailValidation()
    {
        var result = new ErrorListBuilder()
            .AddIf(false, new IdentifierError.Empty(), "id")
            .ToResult();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddIf_TrueCondition_PreservesErrorAndField()
    {
        var result = new ErrorListBuilder()
            .AddIf(true, new IdentifierError.Empty(), "id")
            .ToResult();

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainIssue(new IdentifierError.Empty(), "id"), Assert.Single(result.Error.Issues));
    }

    [Fact]
    public void Add_FailedChecks_PreservesErrorOrderAndFieldPaths()
    {
        var first = UnitResult.Failure(ErrorList.Single(new CatalogueError.Required("name"), "name"));
        var second = UnitResult.Failure(ErrorList.Single(new ClientError.InvalidContactValue(), "contacts[2].value"));

        var result = new ErrorListBuilder()
            .Add(first)
            .Add(UnitResult.Success<ErrorList>())
            .Add(second)
            .ToResult();

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { first.Error.Issues[0], second.Error.Issues[0] }, result.Error.Issues);
    }

    [Fact]
    public void Add_SuccessfulCheck_KeepsBuilderSuccessful()
    {
        var result = new ErrorListBuilder()
            .Add(UnitResult.Success<ErrorList>())
            .ToResult();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddRange_EmptySequence_KeepsBuilderSuccessful()
    {
        var result = new ErrorListBuilder().AddRange([]).ToResult();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AddRange_RepeatedIssues_PreservesEveryIssue()
    {
        var issue = new DomainIssue(new IdentifierError.Empty(), "id");

        var result = new ErrorListBuilder().AddRange([issue, issue]).ToResult();

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { issue, issue }, result.Error.Issues);
    }

    [Fact]
    public void ToResult_BuilderChangedLater_PreservesPreviousSnapshot()
    {
        var builder = new ErrorListBuilder().Add(new IdentifierError.Empty(), "id");
        var first = builder.ToResult();

        builder.Add(new CatalogueError.Required("name"), "name");
        var second = builder.ToResult();

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Single(first.Error.Issues);
        Assert.Equal(2, second.Error.Issues.Length);
    }
}
