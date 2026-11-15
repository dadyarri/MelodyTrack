using System.Collections.Immutable;
using System.Text;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Loads current identity state and policies together; never trusts a role from a token.</summary>
public sealed class StaffPolicyStore(Database database) : IStaffPolicyStore
{
    // Lock order: identity -> relevant role policy -> personal override -> subject row in the caller.
    // Personal policy changes lock the target identity for UPDATE; role changes lock this seeded role row for UPDATE.
    internal static async Task<UnitResult<ErrorList>> AuthorizeClientNotesWriteAsync(DatabaseTransaction transaction,
        StaffId staffId, DateTimeOffset selectedAt, CancellationToken cancellationToken)
        => await AuthorizeWriteAsync(transaction, staffId, StaffPermission.ClientNotesWrite, [selectedAt], cancellationToken);

    internal static async Task<UnitResult<ErrorList>> AuthorizeClientWriteAsync(DatabaseTransaction transaction,
        StaffId staffId, DateTimeOffset selectedAt, CancellationToken cancellationToken)
        => await AuthorizeWriteAsync(transaction, staffId, StaffPermission.ClientsWrite, [selectedAt], cancellationToken,
            requiredDataScope: PermissionDataScope.Available);

    internal static async Task<UnitResult<ErrorList>> AuthorizeWriteAsync(DatabaseTransaction transaction,
        StaffId staffId, StaffPermission permission, IReadOnlyList<DateTimeOffset> recordDates, CancellationToken cancellationToken,
        string? correctionComment = null, PermissionDataScope? requiredDataScope = null)
    {
        var identity = await transaction.Query("""
            SELECT role::text AS "Role", is_blocked AS "Blocked", is_archived AS "Archived", CURRENT_TIMESTAMP AS "CurrentAt"
            FROM public.staff_access_state WHERE staff_id=@id FOR SHARE
            """).WithParameter("id", staffId.Value).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
        if (identity is null || identity.Blocked || identity.Archived)
            return UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden()));
        if (!TryEnum<StaffRole>(identity.Role, out var role))
            return UnitResult.Failure(ErrorList.Single(new ModelError.InvalidState(), "access"));
        var rolePolicy = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        var personal = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        if (role != StaffRole.Superuser)
        {
            var inherited = await transaction.Query("""
                SELECT allowed AS "Allowed", data_scope::text AS "Data", time_scope::text AS "Time"
                FROM public.role_permission_policy
                WHERE role=CAST(@role AS public.staff_access_role) AND permission=CAST(@permission AS public.staff_permission) FOR SHARE
                """).WithParameter("role", identity.Role).WithParameter("permission", Label(permission)).GetSingleOrDefaultAsync<GrantRow>(cancellationToken);
            if (inherited is not null)
            {
                var grant = Grant(inherited.Allowed, inherited.Data, inherited.Time);
                if (grant.IsFailure) return UnitResult.Failure(grant.Error);
                rolePolicy.Add(permission, grant.Value);
            }
            var local = await transaction.Query("""
                SELECT allowed AS "Allowed", data_scope::text AS "Data", time_scope::text AS "Time"
                FROM public.staff_permission_override WHERE staff_id=@id AND permission=CAST(@permission AS public.staff_permission) FOR SHARE
                """).WithParameter("id", staffId.Value).WithParameter("permission", Label(permission)).GetSingleOrDefaultAsync<GrantRow>(cancellationToken);
            if (local is not null)
            {
                var grant = Grant(local.Allowed, local.Data, local.Time);
                if (grant.IsFailure) return UnitResult.Failure(grant.Error);
                personal.Add(permission, grant.Value);
            }
        }
        var actor = new Actor.Staff(staffId, role, StaffPermissions.Resolve(role, rolePolicy.ToImmutable(), personal.ToImmutable()));
        var now = new DateTimeOffset(identity.CurrentAt, TimeSpan.Zero);
        var expensePermission = permission is StaffPermission.ExpensesRecord or StaffPermission.ExpensesCorrect or StaffPermission.ExpensesDelete;
        if (role == StaffRole.Administrator && permission is (StaffPermission.ExpensesCorrect or StaffPermission.ExpensesDelete)
            && string.IsNullOrWhiteSpace(correctionComment))
            return UnitResult.Failure(ErrorList.Single(new CatalogueError.Required("comment"), "comment"));
        return recordDates.Count > 0 && recordDates.All(date => actor.HasPermission(permission, recordAt: date, now: now)
            && (requiredDataScope is null || actor.Permissions is { } permissions && permissions.For(permission).DataScope == requiredDataScope)
            && (!expensePermission || role != StaffRole.Administrator || SchoolTime.Date(date) == SchoolTime.Date(now)))
            ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden()));
    }

    /// <summary>Resolves one current snapshot of role inheritance and individual exceptions.</summary>
    public async Task<Result<Actor.Staff, ErrorList>> ResolveAsync(StaffId staffId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (staffId.Value == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty(), "staffId");
        var rows = await database.Query("""
            SELECT s.role::text AS "Role", s.is_blocked AS "Blocked", s.is_archived AS "Archived", p.permission::text AS "Permission",
                   r.allowed AS "RoleAllowed", r.data_scope::text AS "RoleData", r.time_scope::text AS "RoleTime",
                   o.allowed AS "PersonalAllowed", o.data_scope::text AS "PersonalData", o.time_scope::text AS "PersonalTime"
            FROM public.staff_access_state s
            CROSS JOIN unnest(enum_range(NULL::public.staff_permission)) p(permission)
            LEFT JOIN public.role_permission_policy r ON r.role = s.role AND r.permission = p.permission
            LEFT JOIN public.staff_permission_override o ON o.staff_id = s.staff_id AND o.permission = p.permission
            WHERE s.staff_id = @id
            """).WithParameter("id", staffId.Value).GetAllAsync<AccessRow>(cancellationToken);
        if (rows.Count == 0 || rows[0].Blocked || rows[0].Archived) return ErrorList.Single(new ModelError.Forbidden());
        if (!TryEnum<StaffRole>(rows[0].Role, out var role)) return ErrorList.Single(new ModelError.InvalidState(), "access");
        var rolePolicy = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        var personal = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        foreach (var row in rows)
        {
            if (!TryEnum<StaffPermission>(row.Permission, out var permission)) return ErrorList.Single(new ModelError.InvalidState(), "access");
            if (row.RoleAllowed is { } roleAllowed)
            {
                var grant = Grant(roleAllowed, row.RoleData, row.RoleTime);
                if (grant.IsFailure) return grant.Error;
                rolePolicy.Add(permission, grant.Value);
            }
            if (row.PersonalAllowed is { } personalAllowed)
            {
                var grant = Grant(personalAllowed, row.PersonalData, row.PersonalTime);
                if (grant.IsFailure) return grant.Error;
                personal.Add(permission, grant.Value);
            }
        }
        return new Actor.Staff(staffId, role, StaffPermissions.Resolve(role, rolePolicy.ToImmutable(), personal.ToImmutable()));
    }

    /// <summary>Changes a non-superuser role with an atomic structured history entry and repeat key.</summary>
    public Task<UnitResult<ErrorList>> SetRolePolicyAsync(Guid commandId, StaffId authorId, StaffRole role,
        StaffPermission permission, PermissionGrant grant, CancellationToken cancellationToken) =>
        ChangeAsync(commandId, authorId, role, null, permission, grant, cancellationToken);

    /// <summary>Changes a personal exception; null removes it and resumes live role inheritance.</summary>
    public Task<UnitResult<ErrorList>> SetPersonalPolicyAsync(Guid commandId, StaffId authorId, StaffId targetId,
        StaffPermission permission, PermissionGrant? grant, CancellationToken cancellationToken) =>
        ChangeAsync(commandId, authorId, null, targetId, permission, grant, cancellationToken);

    private async Task<UnitResult<ErrorList>> ChangeAsync(Guid commandId, StaffId authorId, StaffRole? targetRole,
        StaffId? targetId, StaffPermission permission, PermissionGrant? grant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnitResult<ErrorList> Fail(DomainError error, string? field = null) => UnitResult.Failure(ErrorList.Single(error, field));
        if (commandId == Guid.Empty || authorId.Value == Guid.Empty || targetId is { Value: var targetValue } && targetValue == Guid.Empty)
            return Fail(new IdentifierError.Empty());
        if (!Enum.IsDefined(permission) || targetRole is { } role && !Enum.IsDefined(role) || grant?.Validate().IsFailure == true)
            return Fail(new ModelError.InvalidValue());
        if (targetRole is not null && grant is null) return Fail(new ModelError.InvalidValue(), "grant");
        if (targetRole == StaffRole.Superuser || permission == StaffPermission.PoliciesManage && grant?.Allowed == true)
            return Fail(new ModelError.Forbidden());
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var author = await transaction.Query("""
            SELECT role::text AS "Role", is_blocked AS "Blocked", is_archived AS "Archived"
            FROM public.staff_access_state WHERE staff_id = @id FOR SHARE
            """).WithParameter("id", authorId.Value).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
        if (author is not { Role: "superuser", Blocked: false, Archived: false }) return Fail(new ModelError.Forbidden());
        if (targetId is { } staff)
        {
            var target = await transaction.Query("SELECT role::text AS \"Role\" FROM public.staff_access_state WHERE staff_id=@id FOR UPDATE")
                .WithParameter("id", staff.Value).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
            if (target is null) return Fail(new ModelError.NotFound(), "staffId");
            if (target.Role == "superuser") return Fail(new ModelError.Forbidden());
        }
        var beforeQuery = targetId is not null
            ? transaction.Query("SELECT allowed AS \"Allowed\", data_scope::text AS \"Data\", time_scope::text AS \"Time\" FROM public.staff_permission_override WHERE staff_id=@target AND permission=CAST(@permission AS public.staff_permission) FOR UPDATE").WithParameter("target", targetId.Value.Value)
            : transaction.Query("SELECT allowed AS \"Allowed\", data_scope::text AS \"Data\", time_scope::text AS \"Time\" FROM public.role_permission_policy WHERE role=CAST(@role AS public.staff_access_role) AND permission=CAST(@permission AS public.staff_permission) FOR UPDATE").WithParameter("role", Label(targetRole!.Value));
        var before = await beforeQuery.WithParameter("permission", Label(permission)).GetSingleOrDefaultAsync<GrantRow>(cancellationToken);
        if (targetRole is not null && before is null) return Fail(new ModelError.InvalidState(), "rolePolicy");

        var inserted = await transaction.Query("""
            INSERT INTO public.staff_policy_commands(command_id, author_staff_id, target_staff_id, target_role, permission,
                before_allowed, before_data_scope, before_time_scope, after_allowed, after_data_scope, after_time_scope)
            VALUES (@commandId, @authorId, @targetId, CAST(@role AS public.staff_access_role), CAST(@permission AS public.staff_permission),
                @beforeAllowed, CAST(@beforeData AS public.permission_data_scope), CAST(@beforeTime AS public.permission_time_scope),
                @allowed, CAST(@data AS public.permission_data_scope), CAST(@time AS public.permission_time_scope))
            ON CONFLICT (command_id) DO NOTHING RETURNING command_id
            """).WithParameter("commandId", commandId).WithParameter("authorId", authorId.Value)
            .WithParameter("targetId", targetId?.Value).WithParameter("role", targetRole is { } targetRoleValue ? Label(targetRoleValue) : null)
            .WithParameter("permission", Label(permission)).WithParameter("beforeAllowed", before?.Allowed)
            .WithParameter("beforeData", before?.Data).WithParameter("beforeTime", before?.Time)
            .WithParameter("allowed", grant?.Allowed).WithParameter("data", grant is null ? null : Label(grant.DataScope))
            .WithParameter("time", grant is null ? null : Label(grant.TimeScope)).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (inserted is null)
        {
            var previous = await transaction.Query("""
                SELECT author_staff_id AS "AuthorId", target_staff_id AS "TargetId", target_role::text AS "Role",
                    permission::text AS "Permission", after_allowed AS "Allowed", after_data_scope::text AS "Data", after_time_scope::text AS "Time"
                FROM public.staff_policy_commands WHERE command_id=@id
                """).WithParameter("id", commandId).GetSingleAsync<CommandRow>(cancellationToken);
            return previous.AuthorId == authorId.Value && previous.TargetId == targetId?.Value
                && previous.Role == (targetRole is { } replayRole ? Label(replayRole) : null) && previous.Permission == Label(permission)
                && previous.Allowed == grant?.Allowed && previous.Data == (grant is null ? null : Label(grant.DataScope))
                && previous.Time == (grant is null ? null : Label(grant.TimeScope))
                ? UnitResult.Success<ErrorList>() : Fail(new ModelError.InvalidState(), "commandId");
        }
        DatabaseQuery change;
        if (targetRole is { } roleToChange)
            change = transaction.Query("""
                UPDATE public.role_permission_policy SET allowed=@allowed, data_scope=CAST(@data AS public.permission_data_scope), time_scope=CAST(@time AS public.permission_time_scope)
                WHERE role=CAST(@role AS public.staff_access_role) AND permission=CAST(@permission AS public.staff_permission)
                """).WithParameter("role", Label(roleToChange));
        else if (grant is null)
            change = transaction.Query("DELETE FROM public.staff_permission_override WHERE staff_id=@target AND permission=CAST(@permission AS public.staff_permission)")
                .WithParameter("target", targetId!.Value.Value);
        else
            change = transaction.Query("""
                INSERT INTO public.staff_permission_override(staff_id, permission, allowed, data_scope, time_scope)
                VALUES (@target, CAST(@permission AS public.staff_permission), @allowed, CAST(@data AS public.permission_data_scope), CAST(@time AS public.permission_time_scope))
                ON CONFLICT (staff_id, permission) DO UPDATE SET allowed=EXCLUDED.allowed, data_scope=EXCLUDED.data_scope, time_scope=EXCLUDED.time_scope
                """).WithParameter("target", targetId!.Value.Value);
        change = change.WithParameter("permission", Label(permission));
        if (grant is not null) change = change.WithParameter("allowed", grant.Allowed).WithParameter("data", Label(grant.DataScope)).WithParameter("time", Label(grant.TimeScope));
        await change.ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UnitResult.Success<ErrorList>();
    }

    private static Result<PermissionGrant, ErrorList> Grant(bool allowed, string? data, string? time) =>
        TryEnum<PermissionDataScope>(data, out var dataScope) && TryEnum<PermissionTimeScope>(time, out var timeScope)
            ? new PermissionGrant(allowed, dataScope, timeScope) : ErrorList.Single(new ModelError.InvalidState(), "access");

    private static bool TryEnum<T>(string? label, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>()) if (Label(candidate) == label) { value = candidate; return true; }
        value = default;
        return false;
    }

    private static string Label<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        var result = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 && char.IsUpper(text[index])) result.Append('_');
            result.Append(char.ToLowerInvariant(text[index]));
        }
        return result.ToString();
    }

    private sealed class IdentityRow
    {
        public string Role { get; init; } = "";
        public bool Blocked { get; init; }
        public bool Archived { get; init; }
        public DateTime CurrentAt { get; init; }
    }
    private sealed class AccessRow
    {
        public string Role { get; init; } = "";
        public bool Blocked { get; init; }
        public bool Archived { get; init; }
        public string Permission { get; init; } = "";
        public bool? RoleAllowed { get; init; }
        public string? RoleData { get; init; }
        public string? RoleTime { get; init; }
        public bool? PersonalAllowed { get; init; }
        public string? PersonalData { get; init; }
        public string? PersonalTime { get; init; }
    }
    private sealed class GrantRow
    {
        public bool Allowed { get; init; }
        public string? Data { get; init; }
        public string? Time { get; init; }
    }
    private sealed class CommandRow
    {
        public Guid AuthorId { get; init; }
        public Guid? TargetId { get; init; }
        public string? Role { get; init; }
        public string Permission { get; init; } = "";
        public bool? Allowed { get; init; }
        public string? Data { get; init; }
        public string? Time { get; init; }
    }
}
