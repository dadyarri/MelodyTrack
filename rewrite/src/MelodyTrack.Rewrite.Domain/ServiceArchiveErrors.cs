namespace MelodyTrack.Rewrite.Domain;

public closed record ServiceArchiveError(string Code) : DomainError(Code)
{
    public sealed record UnusedPaidLessons() : ServiceArchiveError("service.unused_paid_lessons");
}
