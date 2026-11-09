using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffInvitationTests
{
    [Theory]
    [InlineData(StaffRole.Administrator, true)]
    [InlineData(StaffRole.Administrator, false)]
    [InlineData(StaffRole.Superuser, true)]
    [InlineData(StaffRole.Superuser, false)]
    public void Accept_RequiredSecondFactorUnavailable_LeavesInvitationUnused(StaffRole role, bool hasPassword)
    {
        var staff = MakeStaff().ChangeRole(role);
        var invitation = Ok(StaffInvitationRules.Create(new(Guid(60)), role));
        var login = Ok(StaffLoginRules.Create(staff.Id, "staff", hasPassword, false,
            hasPassword ? [] : ["credential-id"]));

        var result = invitation.Accept(staff, login, Now);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Null(invitation.AcceptedBy);
        Assert.Null(invitation.AcceptedAt);

        var correctedLogin = Ok(StaffLoginRules.Create(staff.Id, "staff", hasPassword, true, ["credential-id"]));
        var accepted = Ok(invitation.Accept(staff, correctedLogin, Now));
        Assert.Equal(staff.Id, accepted.AcceptedBy);
    }

    [Theory]
    [InlineData(StaffRole.Teacher, true, false, false, false, SecondFactorRequirement.None)]
    [InlineData(StaffRole.Teacher, false, false, true, false, SecondFactorRequirement.None)]
    [InlineData(StaffRole.Teacher, true, false, true, true, SecondFactorRequirement.AuthenticatorOrPasskey)]
    [InlineData(StaffRole.Teacher, false, true, true, true, SecondFactorRequirement.Authenticator)]
    [InlineData(StaffRole.Administrator, true, false, true, false, SecondFactorRequirement.AuthenticatorOrPasskey)]
    [InlineData(StaffRole.Administrator, true, true, false, false, SecondFactorRequirement.AuthenticatorOrPasskey)]
    [InlineData(StaffRole.Administrator, false, true, true, false, SecondFactorRequirement.Authenticator)]
    [InlineData(StaffRole.Superuser, true, false, true, false, SecondFactorRequirement.AuthenticatorOrPasskey)]
    [InlineData(StaffRole.Superuser, true, true, false, false, SecondFactorRequirement.AuthenticatorOrPasskey)]
    [InlineData(StaffRole.Superuser, false, true, true, false, SecondFactorRequirement.Authenticator)]
    public void Accept_UsableCredentials_AllowsSignInAndConsumesInvitation(StaffRole role, bool hasPassword,
        bool authenticatorEnabled, bool hasPasskey, bool secondFactorEnabled, SecondFactorRequirement expectedFactor)
    {
        var staff = MakeStaff().ChangeRole(role);
        var invitation = Ok(StaffInvitationRules.Create(new(Guid(60)), role));
        var login = Ok(StaffLoginRules.Create(staff.Id, "staff", hasPassword, authenticatorEnabled,
            hasPasskey ? ["credential-id"] : [], secondFactorEnabled));
        var now = Now.ToOffset(TimeSpan.FromHours(3));

        var accepted = Ok(invitation.Accept(staff, login, now));

        Assert.Equal(staff.Id, accepted.AcceptedBy);
        Assert.Equal(Now, accepted.AcceptedAt);
        Assert.Equal(TimeSpan.Zero, accepted.AcceptedAt!.Value.Offset);
        var method = hasPassword ? StaffSignInMethod.Password : StaffSignInMethod.Passkey;
        Assert.Equal(expectedFactor, Ok(login.RequiredFactor(staff, method)));
        AssertError(new ModelError.InvalidState(), accepted.Accept(staff, login, now));
    }
}
