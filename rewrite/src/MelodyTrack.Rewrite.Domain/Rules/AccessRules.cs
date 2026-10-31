using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class StaffLoginRules
{
    public static Result<StaffLogin, ErrorList> Create(StaffId staffId, string? login, bool hasPassword, bool authenticatorEnabled, IEnumerable<string> passkeyIds, bool secondFactorEnabled = false)
    {
        var name = Validation.Required("login", login);
        var validation = Validation.Collect(Validation.Identifier(staffId.Value, "staffId"), Validation.Check(name));
        if (validation.IsFailure) return validation.Error;
        var keys = passkeyIds.ToImmutableHashSet();
        if (keys.Any(string.IsNullOrWhiteSpace) || !hasPassword && keys.IsEmpty)
            return ErrorList.Single(new ModelError.InvalidValue(), "credentials");
        if (secondFactorEnabled && !authenticatorEnabled && keys.IsEmpty) return ErrorList.Single(new ModelError.InvalidValue(), "secondFactor");
        return new StaffLogin(staffId, name.Value, hasPassword, authenticatorEnabled, keys, secondFactorEnabled || authenticatorEnabled);
    }
}

public static class StaffInvitationRules
{
    public static Result<StaffInvitation, ErrorList> Create(StaffInvitationId id, StaffRole role)
    {
        var validation = Validation.Identifier(id.Value);
        if (validation.IsFailure) return validation.Error;
        return !Enum.IsDefined(role) ? ErrorList.Single(new ModelError.InvalidValue(), "role") : new StaffInvitation(id, role);
    }

    public static Result<StaffInvitation, ErrorList> Accept(StaffInvitation model, Staff staff, StaffLogin login, DateTimeOffset now)
    {
        if (model.AcceptedBy is not null) return ErrorList.Single(new ModelError.InvalidState());
        if (staff.Role != model.Role || staff.Id != login.StaffId)
            return ErrorList.Single(new ModelError.InvalidValue(), "staff");
        return model with { AcceptedBy = staff.Id, AcceptedAt = now.ToUniversalTime() };
    }
}

public static class StaffSessionRules
{
    public static Result<StaffSession, ErrorList> Start(StaffSessionId id, Staff staff, DateTimeOffset now)
    {
        var validation = Validation.Identifier(id.Value);
        if (validation.IsFailure) return validation.Error;

        try { return new StaffSession(id, staff.Id, now); }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static Result<StaffSession, ErrorList> Refresh(this StaffSession model, DateTimeOffset now)
    {
        if (model.IsRevoked || now < model.CreatedAt || now >= model.RefreshExpiresAt) return ErrorList.Single(new ModelError.InvalidState());
        try { return model with { RefreshExpiresAt = now.ToUniversalTime().AddDays(14) }; }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static StaffSession Revoke(this StaffSession model) => model with { IsRevoked = true };
}

public static class ClientAccessRules
{
    public static Result<ClientAccess, ErrorList> Issue(ClientAccessId id, Client client)
    {
        if (client.IsArchived) return ErrorList.Single(new ModelError.Forbidden());
        var validation = Validation.Identifier(id.Value);
        return validation.IsFailure ? validation.Error : new ClientAccess(id, client.Id);
    }

    public static Result<ClientAccess, ErrorList> Revoke(this ClientAccess model) => model with { IsRevoked = true };

    public static ClientAccess RevokeForArchive(this ClientAccess model) => model with { IsRevoked = true };
}

public static class ClientSessionRules
{
    public static Result<ClientSession, ErrorList> Start(ClientSessionId id, ClientAccess access, Client client)
    {
        var validation = Validation.Identifier(id.Value);
        if (validation.IsFailure) return validation.Error;
        if (access.IsRevoked || access.ClientId != client.Id || client.IsArchived) return ErrorList.Single(new ModelError.Forbidden());
        return new ClientSession(id, access);
    }

    public static ClientSession Revoke(this ClientSession model) => model with { IsRevoked = true };
}
