using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class DevicePushPreferenceOperations
{
    public static bool CanDeliver(this DevicePushPreference model, Notification notification, bool activeSessionIsValid) => model.Enabled && activeSessionIsValid &&
        ((model.ActiveAccount, notification.Recipient) switch
        {
            (Actor.Client first, Actor.Client second) => first.Id == second.Id,
            (Actor.Staff first, Actor.Staff second) => first.Id == second.Id,
            _ => false
        });
}

public static class CalendarSubscriptionOperations
{
    public static Result<CalendarSubscription, ErrorList> Create(CalendarSubscriptionId id, Actor owner, CalendarSubject subject, ClientAccess? clientAccess = null)
    {
        if (subject is CalendarSubject.Staff staffSubject && (owner is not Actor.Staff person || person.Id != staffSubject.Id))
            return ErrorList.Single(new ModelError.Forbidden());
        if (subject is CalendarSubject.Client clientSubject && owner is Actor.Client clientOwner && clientOwner.Id != clientSubject.Id)
            return ErrorList.Single(new ModelError.Forbidden());
        return CalendarSubscriptionRules.Create(id, owner, subject, clientAccess);
    }

    public static CalendarSubscription RevokeForStaff(this CalendarSubscription model, Staff staff) => model.Owner is Actor.Staff owner && owner.Id == staff.Id && (staff.IsBlocked || staff.IsArchived)
        ? model.Revoke() : model;

    public static CalendarSubscription RevokeForClient(this CalendarSubscription model, Client client, ClientAccess currentAccess) => model.Subject is CalendarSubject.Client subject
        && subject.Id == client.Id && (client.IsArchived || currentAccess.IsRevoked || currentAccess.ClientId != client.Id || model.ClientAccessId != currentAccess.Id)
            ? model.Revoke() : model;
}

public static class AuditEntryOperations
{
    public static bool CanReadGeneralJournal(this AuditEntry model, Actor actor) => actor.IsSuperuser();
}
