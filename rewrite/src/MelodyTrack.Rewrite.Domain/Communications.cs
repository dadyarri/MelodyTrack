using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public closed record NotificationEvent
{
    public sealed record CourseAssigned(CourseEnrollmentId EnrollmentId) : NotificationEvent;
    public sealed record HomeworkAssigned(PersonalAssignmentId AssignmentId, AppointmentId AppointmentId) : NotificationEvent;
    public sealed record TopicsOpened(CourseEnrollmentId EnrollmentId, ImmutableHashSet<TopicId> TopicIds) : NotificationEvent;
    public sealed record AppointmentCreated(AppointmentId AppointmentId) : NotificationEvent;
    public sealed record AppointmentMoved(AppointmentId AppointmentId, DateTimeOffset PreviousStart, DateTimeOffset CurrentStart) : NotificationEvent;
    public sealed record AppointmentCancelled(AppointmentId AppointmentId) : NotificationEvent;

    internal UnitResult<ErrorList> Validate() => this switch
    {
        CourseAssigned course => Validation.Identifier(course.EnrollmentId.Value, "enrollmentId"),
        HomeworkAssigned homework => Validation.Collect(Validation.Identifier(homework.AssignmentId.Value, "assignmentId"),
            Validation.Identifier(homework.AppointmentId.Value, "appointmentId")),
        TopicsOpened topics => Validation.Collect(Validation.Identifier(topics.EnrollmentId.Value, "enrollmentId"),
            Validation.Collect(topics.TopicIds.Select(topicId => Validation.Identifier(topicId.Value, "topicId")).ToArray())),
        AppointmentCreated created => Validation.Identifier(created.AppointmentId.Value, "appointmentId"),
        AppointmentMoved moved => Validation.Identifier(moved.AppointmentId.Value, "appointmentId"),
        AppointmentCancelled cancelled => Validation.Identifier(cancelled.AppointmentId.Value, "appointmentId")
    };
}

public sealed record Notification
{
    internal Notification(NotificationId id, Actor recipient, NotificationEvent subject, DateTimeOffset createdAt)
    { Id = id; Recipient = recipient; Event = subject; CreatedAt = createdAt.ToUniversalTime(); }

    public NotificationId Id { get; }

    public Actor Recipient { get; }

    public NotificationEvent Event { get; }

    public DateTimeOffset CreatedAt { get; }
}

public sealed record DevicePushPreference(Actor ActiveAccount, bool Enabled)
{

}

public closed record CalendarSubject
{
    public sealed record Staff(StaffId Id) : CalendarSubject;
    public sealed record Client(ClientId Id) : CalendarSubject;
}

public sealed record CalendarSubscription
{
    internal CalendarSubscription(CalendarSubscriptionId id, Actor owner, CalendarSubject subject, ClientAccessId? clientAccessId)
    { Id = id; Owner = owner; Subject = subject; ClientAccessId = clientAccessId; }

    public CalendarSubscriptionId Id { get; }

    public Actor Owner { get; }

    public CalendarSubject Subject { get; }

    public ClientAccessId? ClientAccessId { get; }

    public bool IsRevoked { get; internal init; }
}

// Announcement audiences, lifetime and public display are still unspecified. Management belongs exclusively to server God Mode (D-294).
public sealed record Announcement
{
    internal Announcement(AnnouncementId id, LearningDocument content) { Id = id; Content = content; }

    public AnnouncementId Id { get; }

    public LearningDocument Content { get; }
}

public sealed record AuditLink(string Kind, Guid Id);
public sealed record AuditChange(string Field, string? Before, string? After);
public sealed record AuditFinancialEffect(string Description, decimal CashDelta, SubscriptionId? SubscriptionId, int UnitDelta);
public sealed record AuditEntry
{
    internal AuditEntry(AuditEntryId id, Actor author, DateTimeOffset occurredAt, string operation, ImmutableArray<AuditLink> links,
        ImmutableArray<AuditChange> changes, ImmutableArray<AuditFinancialEffect> financialEffects, string? reason)
    {
        Id = id; Author = author; OccurredAt = occurredAt.ToUniversalTime(); Operation = operation;
        Links = links; Changes = changes; FinancialEffects = financialEffects; Reason = reason;
    }

    public AuditEntryId Id { get; }

    public Actor Author { get; }

    public DateTimeOffset OccurredAt { get; }

    public string Operation { get; }

    public ImmutableArray<AuditLink> Links { get; }

    public ImmutableArray<AuditChange> Changes { get; }

    public ImmutableArray<AuditFinancialEffect> FinancialEffects { get; }

    public string? Reason { get; }
}
