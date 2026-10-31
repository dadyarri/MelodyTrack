using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class CourseTopicRules
{
    public static Result<CourseTopic, ErrorList> Create(TopicId id, string? name, int xp, IEnumerable<TopicId> dependencies, LearningDocument material, LearningDocument homework)
    {
        var text = Validation.Required("name", name);
        var values = dependencies.ToImmutableHashSet();
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text),
            Validation.Collect(values.Select(value => Validation.Identifier(value.Value, "dependencies")).ToArray()));
        if (validation.IsFailure) return validation.Error;
        if (xp < 0) return ErrorList.Single(new ModelError.InvalidValue(), "xp");
        return new CourseTopic(id, text.Value, xp, values, material, homework);
    }
}

public static class CourseRules
{
    public static Result<Course, ErrorList> Create(CourseId id, string? name, IEnumerable<CourseTopic> topics, IEnumerable<CourseLevel> levels)
    {
        var text = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        var values = topics.ToImmutableArray();
        var thresholds = levels.OrderBy(item => item.Threshold).ToImmutableArray();
        if (values.DistinctBy(item => item.Id).Count() != values.Length || thresholds.Select(item => item.Threshold).Distinct().Count() != thresholds.Length)
            return ErrorList.Single(new ModelError.Duplicate());
        if (values.Any(item => item.Dependencies.Any(dependencyId => !values.Any(topic => topic.Id == dependencyId))))
            return ErrorList.Single(new ModelError.NotFound(), "dependencies");
        // Cycle policy and program grouping are open; do not impose an unapproved structure.
        return new Course(id, text.Value, values, thresholds);
    }

    public static Result<Course, ErrorList> ReplaceTopic(this Course model, CourseTopic topic)
    {
        var values = model.Topics.Where(item => item.Id != topic.Id).Append(topic).ToImmutableArray();
        return Create(model.Id, model.Name, values, model.Levels);
    }

    public static Result<Course, ErrorList> RemoveTopic(this Course model, TopicId id)
    {
        if (!model.Topics.Any(item => item.Id == id)) return ErrorList.Single(new ModelError.NotFound());
        if (model.Topics.Any(item => item.Dependencies.Contains(id))) return ErrorList.Single(new LearningError.DependentTopicsExist());
        return model with { Topics = model.Topics.Where(item => item.Id != id).ToImmutableArray() };
    }
}

public static class CourseEnrollmentRules
{
    public static Result<CourseEnrollment, ErrorList> Assign(CourseEnrollmentId id, ClientId clientId, Course course, IEnumerable<StaffId> teachers, IEnumerable<CourseEnrollment> existing)
    {
        var values = teachers.ToImmutableHashSet();
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Collect(values.Select(value => Validation.Identifier(value.Value, "teachers")).ToArray()));
        if (validation.IsFailure) return validation.Error;
        if (values.IsEmpty) return ErrorList.Single(new ModelError.InvalidValue(), "teachers");
        if (existing.Any(item => item.ClientId == clientId && item.CourseId == course.Id && !item.IsCompleted))
            return ErrorList.Single(new LearningError.ActiveEnrollmentExists());
        return new CourseEnrollment(id, clientId, course.Id, values);
    }

    public static TopicStatus StatusOf(this CourseEnrollment model, TopicId id) => model.Progress.GetValueOrDefault(id, TopicStatus.NotStarted);

    public static bool IsOpen(this CourseEnrollment model, CourseTopic topic) => model.StatusOf(topic.Id) != TopicStatus.NotStarted
        || topic.Dependencies.All(id => model.StatusOf(id) == TopicStatus.Mastered);

    public static long Xp(this CourseEnrollment model, Course course) => course.Id == model.CourseId
        ? course.Topics.Where(topic => model.StatusOf(topic.Id) == TopicStatus.Mastered).Sum(topic => (long)topic.Xp) : 0;

    public static CourseLevel? Level(this CourseEnrollment model, Course course) => course.Id == model.CourseId ? course.Levels.LastOrDefault(level => level.Threshold <= model.Xp(course)) : null;

    public static ImmutableArray<TopicId> TopicsRelockedBy(this CourseEnrollment model, Course course, TopicId topicId, TopicStatus status)
    {
        var proposed = model with { Progress = model.Progress.SetItem(topicId, status) };
        return course.Topics.Where(topic => model.IsOpen(topic) && !proposed.IsOpen(topic)).Select(topic => topic.Id).ToImmutableArray();
    }

    public static Result<CourseEnrollment, ErrorList> ChangeStatus(this CourseEnrollment model, Course course, TopicId topicId, TopicStatus status, bool confirmRelocking, IEnumerable<CourseEnrollment> otherEnrollments)
    {
        if (course.Id != model.CourseId) return ErrorList.Single(new ModelError.InvalidValue(), "courseId");
        var topic = course.Topics.FirstOrDefault(item => item.Id == topicId);
        if (topic is null) return ErrorList.Single(new ModelError.NotFound());
        if (!Enum.IsDefined(status)) return ErrorList.Single(new ModelError.InvalidValue(), "status");
        if (!model.IsOpen(topic)) return ErrorList.Single(new LearningError.TopicLocked());
        if (!confirmRelocking && !model.TopicsRelockedBy(course, topicId, status).IsEmpty)
            return ErrorList.Single(new LearningError.ConfirmRelockingRequired());
        var changed = model with { Progress = model.Progress.SetItem(topicId, status) };
        if (model.IsCompleted && model.StatusOf(topicId) == TopicStatus.Mastered && status != TopicStatus.Mastered)
        {
            if (otherEnrollments.Any(item => item.Id != model.Id && item.ClientId == model.ClientId && item.CourseId == model.CourseId && !item.IsCompleted))
                return ErrorList.Single(new LearningError.ActiveEnrollmentExists());
            changed = changed with { IsCompleted = false };
        }
        if (course.Topics.Length > 0 && course.Topics.All(item => changed.StatusOf(item.Id) == TopicStatus.Mastered))
            changed = changed with { IsCompleted = true };
        return changed;
    }

    public static Result<CourseEnrollment, ErrorList> ReplaceTeachers(this CourseEnrollment model, IEnumerable<StaffId> teachers)
    {
        var values = teachers.ToImmutableHashSet();
        if (values.IsEmpty) return ErrorList.Single(new ModelError.InvalidValue(), "teachers");
        var validation = Validation.Collect(values.Select(value => Validation.Identifier(value.Value, "teachers")).ToArray());
        if (validation.IsFailure) return validation.Error;
        return model with { Teachers = values };
    }

    public static Result<CourseEnrollment, ErrorList> ApplyProgram(this CourseEnrollment model, Course course)
    {
        if (course.Id != model.CourseId) return ErrorList.Single(new ModelError.InvalidValue(), "courseId");
        return model with { IsCompleted = model.IsCompleted || course.Topics.Length > 0 && course.Topics.All(topic => model.StatusOf(topic.Id) == TopicStatus.Mastered) };
    }
}

public static class PersonalAssignmentRules
{
    public static Result<PersonalAssignment, ErrorList> Create(PersonalAssignmentId id, Appointment appointment, LearningDocument content)
    {
        if (appointment.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        var validation = Validation.Identifier(id.Value);
        return validation.IsFailure ? validation.Error : new PersonalAssignment(id, appointment, content);
    }

    public static Result<PersonalAssignment, ErrorList> Edit(this PersonalAssignment model, Appointment appointment, LearningDocument content)
    {
        if (appointment.Id != model.AppointmentId || appointment.ClientId != model.ClientId) return ErrorList.Single(new ModelError.WrongOwner());
        if (appointment.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        return model with { Content = content };
    }

    public static Result<PersonalAssignment, ErrorList> SetCompleted(this PersonalAssignment model, Appointment appointment, bool completed)
    {
        if (appointment.Id != model.AppointmentId || appointment.ClientId != model.ClientId) return ErrorList.Single(new ModelError.WrongOwner());
        return model with { IsCompleted = completed };
    }
}
