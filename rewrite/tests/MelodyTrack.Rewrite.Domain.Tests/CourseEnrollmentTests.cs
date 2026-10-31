using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class CourseEnrollmentTests
{
    private static Actor Teacher => new Actor.Staff(TestData.StaffId, StaffRole.Teacher);
    private static CourseTopic Topic(int id, params TopicId[] dependencies) => Ok(CourseTopicRules.Create(new(Guid(id)), "Тема", 10,
        dependencies, LearningDocument.Empty, LearningDocument.Empty));
    private static Course Program(params CourseTopic[] topics) => Ok(CourseRules.Create(new(Guid(100)), "Гитара", topics, []));
    private static CourseEnrollment Enrollment(Course course) => Ok(CourseEnrollmentRules.Assign(new(Guid(101)), TestData.ClientId, course, [TestData.StaffId], []));

    [Fact]
    public void ChangeStatus_AllTopicsMastered_CompletesAndComputesXp()
    {
        var topic = Topic(102);
        var course = Program(topic);

        var enrollment = Ok(Enrollment(course).ChangeStatus(course, topic.Id, TopicStatus.Mastered, Teacher, false, []));

        Assert.True(enrollment.IsCompleted);
        Assert.Equal(10L, enrollment.Xp(course));
    }

    [Fact]
    public void ChangeStatus_DependencyRollback_RequiresConfirmationBeforeRelocking()
    {
        var first = Topic(102);
        var second = Topic(103, first.Id);
        var course = Program(first, second);
        var enrollment = Ok(Enrollment(course).ChangeStatus(course, first.Id, TopicStatus.Mastered, Teacher, false, []));

        AssertError(new LearningError.ConfirmRelockingRequired(), enrollment.ChangeStatus(course, first.Id, TopicStatus.Learning, Teacher, false, []));
        var changed = Ok(enrollment.ChangeStatus(course, first.Id, TopicStatus.Learning, Teacher, true, []));

        Assert.False(changed.IsOpen(second));
        Assert.Equal(TopicStatus.NotStarted, changed.StatusOf(second.Id));
    }

    [Fact]
    public void ChangeStatus_DependencyRollback_PreservesStartedDependentTopic()
    {
        var first = Topic(102);
        var second = Topic(103, first.Id);
        var course = Program(first, second);
        var enrollment = Ok(Enrollment(course).ChangeStatus(course, first.Id, TopicStatus.Mastered, Teacher, false, []));
        enrollment = Ok(enrollment.ChangeStatus(course, second.Id, TopicStatus.Learning, Teacher, false, []));

        var changed = Ok(enrollment.ChangeStatus(course, first.Id, TopicStatus.Learning, Teacher, false, []));

        Assert.True(changed.IsOpen(second));
        Assert.Equal(TopicStatus.Learning, changed.StatusOf(second.Id));
    }

    [Fact]
    public void ApplyProgram_NewTopic_PreservesCompletedEnrollment()
    {
        var first = Topic(102);
        var course = Program(first);
        var completed = Ok(Enrollment(course).ChangeStatus(course, first.Id, TopicStatus.Mastered, Teacher, false, []));

        var updated = Ok(completed.ApplyProgram(Ok(course.ReplaceTopic(Topic(103)))));

        Assert.True(updated.IsCompleted);
    }

    [Fact]
    public void SelectTopic_LockedTopic_RejectsEvenForFutureAppointment()
    {
        var first = Topic(102);
        var second = Topic(103, first.Id);
        var course = Program(first, second);

        AssertError(new LearningError.TopicLocked(), MakeAppointment(104, Utc(30, 15)).SelectTopic(Enrollment(course), course, second.Id, Teacher));
    }
}
