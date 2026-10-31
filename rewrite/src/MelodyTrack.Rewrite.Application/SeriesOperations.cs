using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public static class SeriesOperations
{
    public static Result<CompensationRestoration, ErrorList> RestoreWithReplacement(StaffRole role, DateTimeOffset now, bool hasBlockers, Appointment original, Appointment replacement)
    {
        if (original.Series is not { } source || replacement.Series is not { } tail || source.SeriesId != tail.SeriesId || tail.Compensates != source.SequenceNumber)
            return ErrorList.Single(new AppointmentError.ReplacementRequiresReview());
        var restored = original.RestoreAfterAbsence(role, now, hasBlockers);
        if (restored.IsFailure) return restored.Error;
        var cancelled = replacement.CancelAsCompensationReplacement(role, now);
        if (cancelled.IsFailure) return cancelled.Error;
        return new CompensationRestoration(restored.Value, cancelled.Value);
    }
}
