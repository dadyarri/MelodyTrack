using MelodyTrack.Rewrite.Domain.Scheduling;

namespace MelodyTrack.Rewrite.Application;

public closed record OperationError
{
    public sealed record Validation(ErrorList Errors) : OperationError;
    public sealed record Conflict(ErrorList Errors) : OperationError;
    public sealed record NotFound : OperationError;
    public sealed record Forbidden : OperationError;
}
