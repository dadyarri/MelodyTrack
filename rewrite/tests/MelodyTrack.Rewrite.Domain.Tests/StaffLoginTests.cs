using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffLoginTests
{
    [Fact]
    public void Create_PasskeyOnlyWithEnabledSecondFactorWithoutAuthenticator_RejectsCredentials()
    {
        var result = StaffLoginRules.Create(TestData.StaffId, "teacher", false, false, ["credential-id"], true);

        AssertError(new ModelError.InvalidValue(), result);
        Assert.Contains(result.Error.Issues, issue => issue.Field == "secondFactor");
    }

    [Fact]
    public void RequiredFactor_PasskeyPrimaryForAdministrator_RequiresAuthenticator()
    {
        var staff = MakeStaff().ChangeRole(StaffRole.Administrator);
        var login = Ok(StaffLoginRules.Create(staff.Id, "admin", true, true, ["credential-id"]));

        Assert.Equal(SecondFactorRequirement.Authenticator, Ok(login.RequiredFactor(staff, StaffSignInMethod.Passkey)));
    }

    [Fact]
    public void RequiredFactor_RememberedTeacherWithOptionalPasskeyFactor_RequiresFactor()
    {
        var staff = MakeStaff();
        var login = Ok(StaffLoginRules.Create(staff.Id, "teacher", true, false, ["credential-id"], true));

        Assert.Equal(SecondFactorRequirement.AuthenticatorOrPasskey, Ok(login.RequiredFactor(staff, StaffSignInMethod.RememberedSession)));
    }
}
