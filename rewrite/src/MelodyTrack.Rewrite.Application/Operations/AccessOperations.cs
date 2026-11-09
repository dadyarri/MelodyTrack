using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class StaffLoginOperations
{
    public static Result<SecondFactorRequirement, ErrorList> RequiredFactor(this StaffLogin model, Staff staff, StaffSignInMethod method)
    {
        if (staff.Id != model.StaffId || staff.IsBlocked || staff.IsArchived) return ErrorList.Single(new ModelError.Forbidden());
        if (!Enum.IsDefined(method) || method == StaffSignInMethod.Password && !model.HasPassword || method == StaffSignInMethod.Passkey && model.PasskeyIds.IsEmpty)
            return ErrorList.Single(new ModelError.InvalidState());
        var required = staff.Role is StaffRole.Superuser or StaffRole.Administrator || model.SecondFactorEnabled;
        if (!required) return SecondFactorRequirement.None;
        if (method == StaffSignInMethod.Passkey)
            return model.AuthenticatorEnabled ? SecondFactorRequirement.Authenticator : ErrorList.Single(new ModelError.InvalidState());
        if (!model.AuthenticatorEnabled && model.PasskeyIds.IsEmpty) return ErrorList.Single(new ModelError.InvalidState());
        return SecondFactorRequirement.AuthenticatorOrPasskey;
    }
}

public static class StaffInvitationOperations
{
    public static Result<StaffInvitation, ErrorList> Create(StaffInvitationId id, StaffRole role, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return StaffInvitationRules.Create(id, role);
    }

    public static Result<StaffInvitation, ErrorList> Accept(this StaffInvitation model, Staff staff, StaffLogin login, DateTimeOffset now)
    {
        var accepted = StaffInvitationRules.Accept(model, staff, login, now);
        if (accepted.IsFailure) return accepted.Error;
        if (staff.IsBlocked || staff.IsArchived) return ErrorList.Single(new ModelError.InvalidValue(), "staff");
        var method = login.HasPassword ? StaffSignInMethod.Password : StaffSignInMethod.Passkey;
        var factor = login.RequiredFactor(staff, method);
        if (factor.IsFailure) return factor.Error;
        return accepted;
    }
}

public static class StaffSessionOperations
{
    public static Result<StaffSession, ErrorList> Start(StaffSessionId id, Staff staff, DateTimeOffset now)
    {
        if (staff.IsBlocked || staff.IsArchived) return ErrorList.Single(new ModelError.Forbidden());
        return StaffSessionRules.Start(id, staff, now);
    }

    public static bool CanUse(this StaffSession model, Staff current, DateTimeOffset now) => current.Id == model.StaffId && !current.IsBlocked && !current.IsArchived
        && !model.IsRevoked && model.CreatedAt <= now && now < model.RefreshExpiresAt;

    public static Result<StaffSession, ErrorList> Refresh(this StaffSession model, Staff current, DateTimeOffset now)
    {
        if (!model.CanUse(current, now)) return ErrorList.Single(new ModelError.Forbidden());
        return StaffSessionRules.Refresh(model, now);
    }
}

public static class ClientAccessOperations
{
    public static Result<ClientAccess, ErrorList> Issue(ClientAccessId id, Client client, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return ClientAccessRules.Issue(id, client);
    }

    public static Result<ClientAccess, ErrorList> Revoke(this ClientAccess model, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return ClientAccessRules.Revoke(model);
    }
}

public static class ClientSessionOperations
{
    public static bool CanUse(this ClientSession model, ClientAccess currentAccess, Client client) => !model.IsRevoked && !currentAccess.IsRevoked && !client.IsArchived
        && model.AccessId == currentAccess.Id && model.ClientId == currentAccess.ClientId && model.ClientId == client.Id;
}
