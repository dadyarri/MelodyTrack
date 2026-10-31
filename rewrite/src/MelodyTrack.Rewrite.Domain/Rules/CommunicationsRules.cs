using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class NotificationRules
{
    public static Result<Notification, ErrorList> Create(NotificationId id, Actor recipient, NotificationEvent subject, DateTimeOffset now)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), recipient.Validate(), subject.Validate());
        if (validation.IsFailure) return validation.Error;
        if (subject is NotificationEvent.CourseAssigned or NotificationEvent.HomeworkAssigned or NotificationEvent.TopicsOpened)
        {
            if (recipient is not Actor.Client) return ErrorList.Single(new ModelError.WrongOwner());
        }
        else if (recipient is not Actor.Staff) return ErrorList.Single(new ModelError.WrongOwner());
        return new Notification(id, recipient, subject, now);
    }
}

public static class CalendarSubscriptionRules
{
    public static Result<CalendarSubscription, ErrorList> Create(CalendarSubscriptionId id, Actor owner, CalendarSubject subject, ClientAccess? clientAccess = null)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), owner.Validate());
        if (validation.IsFailure) return validation.Error;
        if (subject is CalendarSubject.Staff staff)
        {
            var validSubject = Validation.Identifier(staff.Id.Value, "staffId");
            if (validSubject.IsFailure) return validSubject.Error;

        }
        else if (subject is CalendarSubject.Client client)
        {
            if (clientAccess is null || clientAccess.IsRevoked || clientAccess.ClientId != client.Id) return ErrorList.Single(new ModelError.Forbidden());

        }
        return new CalendarSubscription(id, owner, subject, subject is CalendarSubject.Client ? clientAccess?.Id : null);
    }

    public static CalendarSubscription Revoke(this CalendarSubscription model) => model with { IsRevoked = true };

    public static bool Matches(this CalendarSubscription model, Appointment appointment) => !model.IsRevoked && !appointment.IsDeleted && appointment.Status != AppointmentStatus.Cancelled
        && (model.Subject switch
        {
            CalendarSubject.Staff staff => staff.Id == appointment.StaffId,
            CalendarSubject.Client client => client.Id == appointment.ClientId
        });
}

public static class AnnouncementRules
{
    public static Result<Announcement, ErrorList> Create(AnnouncementId id, LearningDocument content)
    {
        var validation = Validation.Identifier(id.Value);
        return validation.IsFailure ? validation.Error : new Announcement(id, content);
    }
}

public static class AuditEntryRules
{
    public static Result<AuditEntry, ErrorList> Create(AuditEntryId id, Actor author, DateTimeOffset occurredAt, string? operation, IEnumerable<AuditLink> links, IEnumerable<AuditChange> changes, IEnumerable<AuditFinancialEffect> effects, string? reason)
    {
        var text = Validation.Required("operation", operation);
        var validation = Validation.Collect(Validation.Identifier(id.Value), author.Validate(), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        return new AuditEntry(id, author, occurredAt, text.Value, links.ToImmutableArray(), changes.ToImmutableArray(),
            effects.ToImmutableArray(), Validation.OptionalText(reason));
    }
}
