using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class PracticeAttemptRules
{
    public static Result<PracticeAttempt, ErrorList> Start(PracticeAttemptId id, Exercise exercise, PracticeContext context, DateTimeOffset now)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), context.Validate());
        return validation.IsFailure ? validation.Error : new PracticeAttempt(id, exercise, context, now);
    }

    private static Result<PracticeAttempt, ErrorList> Transition(this PracticeAttempt model, DateTimeOffset now, PracticeState state, int repetitions)
    {
        if (model.State is PracticeState.Finished or PracticeState.Interrupted) return ErrorList.Single(new ModelError.InvalidState());
        if (now < model.LastChangedAt || repetitions < model.Repetitions || model.State == PracticeState.Paused && repetitions != model.Repetitions)
            return ErrorList.Single(new ModelError.InvalidValue(), "practice");
        TimeSpan duration;
        try { duration = model.ActiveDuration + (model.State == PracticeState.Running ? now - model.LastChangedAt : TimeSpan.Zero); }
        catch (OverflowException) { return ErrorList.Single(new TimeError.OutOfRange()); }
        return model with
        {
            ActiveDuration = duration,
            Repetitions = repetitions,
            LastChangedAt = now.ToUniversalTime(),
            State = state,
            FinishedAt = state is PracticeState.Finished or PracticeState.Interrupted ? now.ToUniversalTime() : null
        };
    }

    public static Result<PracticeAttempt, ErrorList> Pause(this PracticeAttempt model, DateTimeOffset now, int repetitions) => model.State == PracticeState.Running
        ? model.Transition(now, PracticeState.Paused, repetitions) : ErrorList.Single(new ModelError.InvalidState());

    public static Result<PracticeAttempt, ErrorList> Resume(this PracticeAttempt model, DateTimeOffset now) => model.State == PracticeState.Paused
        ? model.Transition(now, PracticeState.Running, model.Repetitions) : ErrorList.Single(new ModelError.InvalidState());

    public static Result<PracticeAttempt, ErrorList> Stop(this PracticeAttempt model, DateTimeOffset now, int repetitions) => model.Transition(now, PracticeState.Finished, repetitions);

    public static Result<PracticeAttempt, ErrorList> Interrupt(this PracticeAttempt model, DateTimeOffset now, int repetitions) => model.Transition(now, PracticeState.Interrupted, repetitions);

    public static Result<TrainerConfiguration, ErrorList> CopyFreeConfiguration(this PracticeAttempt model, bool trainerIsRunning)
    {
        if (model.Context is not PracticeContext.Free) return ErrorList.Single(new ModelError.Forbidden());
        if (trainerIsRunning) return ErrorList.Single(new ModelError.InvalidState());
        return model.Exercise.Configuration;
    }

    public static Result<TimeSpan, ErrorList> VideoDuration(this PracticeAttempt model, TimeSpan? loopDurationOverride = null)
    {

        if (model.State is not (PracticeState.Finished or PracticeState.Interrupted)) return ErrorList.Single(new ModelError.InvalidState());
        if (!model.Exercise.Configuration.Loop && loopDurationOverride is not null) return ErrorList.Single(new ModelError.InvalidValue(), "duration");
        var duration = model.Exercise.Configuration.Loop ? loopDurationOverride ?? model.Exercise.Configuration.CycleDuration : model.ActiveDuration;
        return duration <= TimeSpan.Zero ? ErrorList.Single(new ModelError.InvalidValue(), "duration") : duration;
    }

    public static bool GoalReached(this PracticeAttempt model) => model.Exercise.Goal switch
    {
        ExerciseGoal.Duration duration => model.ActiveDuration >= duration.Value,
        ExerciseGoal.Repetitions repetitions => model.Repetitions >= repetitions.Value,
        null => false
    };
}
