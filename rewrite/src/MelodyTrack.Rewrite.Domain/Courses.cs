using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum TopicStatus { NotStarted, Learning, AwaitingHomeworkReview, Mastered }

public sealed record CourseTopic
{
    internal CourseTopic(TopicId id, string name, int xp, ImmutableHashSet<TopicId> dependencies,
        LearningDocument material, LearningDocument homework)
    { Id = id; Name = name; Xp = xp; Dependencies = dependencies; Material = material; Homework = homework; }

    public TopicId Id { get; }

    public string Name { get; }

    public int Xp { get; }

    public ImmutableHashSet<TopicId> Dependencies { get; }

    public LearningDocument Material { get; }

    public LearningDocument Homework { get; }
}

public sealed record CourseLevel
{
    private CourseLevel(string name, long threshold) { Name = name; Threshold = threshold; }
    public string Name { get; }
    public long Threshold { get; }
    public static Result<CourseLevel, ErrorList> Create(string? name, long threshold)
    {
        var text = Validation.Required("name", name);
        if (text.IsFailure) return text.Error;
        return threshold < 0 ? ErrorList.Single(new ModelError.InvalidValue(), "threshold") : new CourseLevel(text.Value, threshold);
    }
}

public sealed record Course
{
    internal Course(CourseId id, string name, ImmutableArray<CourseTopic> topics, ImmutableArray<CourseLevel> levels)
    { Id = id; Name = name; Topics = topics; Levels = levels; }

    public CourseId Id { get; }

    public string Name { get; }

    public ImmutableArray<CourseTopic> Topics { get; internal init; }

    public ImmutableArray<CourseLevel> Levels { get; }
}

public sealed record CourseEnrollment
{
    internal CourseEnrollment(CourseEnrollmentId id, ClientId clientId, CourseId courseId, ImmutableHashSet<StaffId> teachers)
    { Id = id; ClientId = clientId; CourseId = courseId; Teachers = teachers; }

    public CourseEnrollmentId Id { get; }

    public ClientId ClientId { get; }

    public CourseId CourseId { get; }

    public ImmutableHashSet<StaffId> Teachers { get; internal init; }

    public ImmutableDictionary<TopicId, TopicStatus> Progress { get; internal init; } = ImmutableDictionary<TopicId, TopicStatus>.Empty;

    public bool IsCompleted { get; internal init; }
}

public sealed record LessonTopic(CourseEnrollmentId EnrollmentId, TopicId TopicId);

public sealed record PersonalAssignment
{
    internal PersonalAssignment(PersonalAssignmentId id, Appointment appointment, LearningDocument content)
    { Id = id; AppointmentId = appointment.Id; ClientId = appointment.ClientId; Content = content; }

    public PersonalAssignmentId Id { get; }

    public AppointmentId AppointmentId { get; }

    public ClientId ClientId { get; }

    public LearningDocument Content { get; internal init; }

    public bool IsCompleted { get; internal init; }
}
