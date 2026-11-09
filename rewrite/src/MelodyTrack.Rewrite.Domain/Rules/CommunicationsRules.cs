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
    public static Result<Announcement, ErrorList> Create(AnnouncementId id, LearningDocument content,
        AnnouncementAudience audience, DateTimeOffset endsAt, bool canDismiss)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value),
            Validation.Collect(audience.StaffIds.Select(staffId => Validation.Identifier(staffId.Value, "staffId")).ToArray()),
            Validation.Collect(audience.ClientIds.Select(clientId => Validation.Identifier(clientId.Value, "clientId")).ToArray()));
        if (validation.IsFailure) return validation.Error;
        if (!audience.AllStaff && !audience.AllClients && !audience.BeforeSignIn && audience.StaffIds.IsEmpty && audience.ClientIds.IsEmpty)
            return ErrorList.Single(new ModelError.InvalidValue(), "audience");
        return new Announcement(id, content, audience, endsAt, canDismiss);
    }

    public static bool IsVisibleTo(this Announcement model, Actor? viewer, DateTimeOffset now, bool dismissed) =>
        now < model.EndsAt && !(model.CanDismiss && dismissed) && (viewer switch
        {
            null => model.Audience.BeforeSignIn,
            Actor.Staff staff => model.Audience.AllStaff || model.Audience.StaffIds.Contains(staff.Id),
            Actor.Client client => model.Audience.AllClients || model.Audience.ClientIds.Contains(client.Id)
        });
}

public static class AuditEntryRules
{
    public static Result<AuditEntry, ErrorList> ForAppointmentStateChange(AuditEntryId id, Actor author, Appointment appointment)
    {
        if (appointment.StateHistory.IsEmpty) return ErrorList.Single(new ModelError.InvalidState(), "history");
        var change = appointment.StateHistory[^1];
        if (author is not Actor.Staff staff || staff.Id != change.AuthorId)
            return ErrorList.Single(new ModelError.WrongOwner(), "author");
        return Create(id, author, change.OccurredAt, "appointment.state.changed", [new("appointment", appointment.Id.Value)],
            [new AuditChange.AppointmentStateChanged(change.Before, change.After, change.WasDeleted, change.IsDeleted)], [], null);
    }

    public static Result<AuditEntry, ErrorList> ForExpenseChange(AuditEntryId id, Actor author, Expense? before, Expense? after, DateTimeOffset now)
    {
        if (author is not Actor.Staff) return ErrorList.Single(new ModelError.Forbidden());
        if (before is null && after is null || before is not null && after is not null && before.Id != after.Id)
            return ErrorList.Single(new ModelError.InvalidValue(), "expense");
        var changes = ImmutableArray.CreateBuilder<AuditChange>();
        if (before?.Amount != after?.Amount) changes.Add(new AuditChange.ExpenseAmountChanged(before?.Amount, after?.Amount));
        if (before?.Date != after?.Date) changes.Add(new AuditChange.ExpenseDateChanged(before?.Date, after?.Date));
        if (before?.CategoryId != after?.CategoryId) changes.Add(new AuditChange.ExpenseCategoryChanged(before?.CategoryId, after?.CategoryId));
        if (before?.Description != after?.Description) changes.Add(new AuditChange.ExpenseDescriptionChanged());
        return Create(id, author, now, "expense.changed", [new("expense", (after ?? before)!.Id.Value)], changes, [], null);
    }

    public static Result<AuditEntry, ErrorList> ForClientNotesChange(AuditEntryId id, Actor author, ClientNotesChange change)
    {
        if (author is not Actor.Staff staff || staff.Id != change.AuthorId)
            return ErrorList.Single(new ModelError.WrongOwner(), "author");
        return Create(id, author, change.ChangedAt, "client.notes.changed", [new("client", change.ClientId.Value)],
            [new AuditChange.NotesChanged()], [], null);
    }

    private static Result<AuditEntry, ErrorList> Create(AuditEntryId id, Actor author, DateTimeOffset occurredAt, string? operation, IEnumerable<AuditLink> links, IEnumerable<AuditChange> changes, IEnumerable<AuditFinancialEffect> effects, string? reason)
    {
        var text = Validation.Required("operation", operation);
        var validation = Validation.Collect(Validation.Identifier(id.Value), author.Validate(), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        return new AuditEntry(id, author, occurredAt, text.Value, links.ToImmutableArray(), changes.ToImmutableArray(),
            effects.ToImmutableArray(), Validation.OptionalText(reason));
    }
}
