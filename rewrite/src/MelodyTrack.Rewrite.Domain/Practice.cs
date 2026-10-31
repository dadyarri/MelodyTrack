using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

/// <summary>Historical trainer parameters; concrete trainer schemas belong to their implementations.</summary>
public sealed record TrainerConfiguration
{
    private TrainerConfiguration(string trainer, int bpm, bool loop, bool metronome, TimeSpan cycleDuration,
        ImmutableDictionary<string, string> parameters)
    { Trainer = trainer; Bpm = bpm; Loop = loop; Metronome = metronome; CycleDuration = cycleDuration; Parameters = parameters; }
    public string Trainer { get; }
    public int Bpm { get; }
    public bool Loop { get; }
    public bool Metronome { get; }
    public TimeSpan CycleDuration { get; }
    public ImmutableDictionary<string, string> Parameters { get; }
    public static Result<TrainerConfiguration, ErrorList> Create(string? trainer, int bpm, bool loop, bool metronome,
        TimeSpan cycleDuration, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var name = Validation.Required("trainer", trainer);
        if (name.IsFailure) return name.Error;
        if (bpm <= 0 || cycleDuration <= TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "configuration");
        var values = parameters.ToArray();
        if (values.Any(item => string.IsNullOrWhiteSpace(item.Key)) || values.Select(item => item.Key).Distinct().Count() != values.Length)
            return ErrorList.Single(new ModelError.InvalidValue(), "parameters");
        return new TrainerConfiguration(name.Value, bpm, loop, metronome, cycleDuration, values.ToImmutableDictionary());
    }
}

public closed record ExerciseGoal
{
    public sealed record Duration : ExerciseGoal
    {
        private Duration(TimeSpan value) => Value = value;
        public TimeSpan Value { get; }
        public static Result<Duration, ErrorList> Create(TimeSpan value) => value <= TimeSpan.Zero
            ? ErrorList.Single(new ModelError.InvalidValue(), "duration") : new Duration(value);
    }
    public sealed record Repetitions : ExerciseGoal
    {
        private Repetitions(int value) => Value = value;
        public int Value { get; }
        public static Result<Repetitions, ErrorList> Create(int value) => value <= 0
            ? ErrorList.Single(new ModelError.InvalidValue(), "repetitions") : new Repetitions(value);
    }
}

public sealed record Exercise
{
    private Exercise(ExerciseId id, TrainerConfiguration configuration, ExerciseGoal? goal)
    { Id = id; Configuration = configuration; Goal = goal; }
    public ExerciseId Id { get; }
    public TrainerConfiguration Configuration { get; }
    public ExerciseGoal? Goal { get; }
    public static Result<Exercise, ErrorList> Create(ExerciseId id, TrainerConfiguration configuration, ExerciseGoal? goal)
    {
        var validation = Validation.Identifier(id.Value);
        return validation.IsFailure ? validation.Error : new Exercise(id, configuration, goal);
    }
}

public enum LearningExerciseLocation { Material, Homework }
public closed record PracticeContext
{
    public sealed record Free(Actor Owner) : PracticeContext;
    public sealed record Course(ClientId ClientId, CourseEnrollmentId EnrollmentId, TopicId TopicId,
        LearningExerciseLocation Location) : PracticeContext;
    public sealed record PersonalHomework(ClientId ClientId, PersonalAssignmentId AssignmentId, AppointmentId AppointmentId) : PracticeContext;

    internal UnitResult<ErrorList> Validate() => this switch
    {
        Free free => free.Owner.Validate(),
        Course course => Validation.Collect(Validation.Identifier(course.ClientId.Value, "clientId"),
            Validation.Identifier(course.EnrollmentId.Value, "enrollmentId"), Validation.Identifier(course.TopicId.Value, "topicId")),
        PersonalHomework homework => Validation.Collect(Validation.Identifier(homework.ClientId.Value, "clientId"),
            Validation.Identifier(homework.AssignmentId.Value, "assignmentId"), Validation.Identifier(homework.AppointmentId.Value, "appointmentId"))
    };
}
public enum PracticeState { Running, Paused, Finished, Interrupted }

public sealed record PracticeAttempt
{
    internal PracticeAttempt(PracticeAttemptId id, Exercise exercise, PracticeContext context, DateTimeOffset startedAt)
    { Id = id; Exercise = exercise; Context = context; StartedAt = startedAt.ToUniversalTime(); LastChangedAt = StartedAt; }

    public PracticeAttemptId Id { get; }

    public Exercise Exercise { get; }

    public PracticeContext Context { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset LastChangedAt { get; internal init; }

    public DateTimeOffset? FinishedAt { get; internal init; }

    public TimeSpan ActiveDuration { get; internal init; }

    public int Repetitions { get; internal init; }

    public PracticeState State { get; internal init; } = PracticeState.Running;
}
