using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class ActorRules
{
    public static UnitResult<ErrorList> Validate(this Actor actor) => actor switch
    {
        Actor.Staff staff => Validation.Collect(Validation.Identifier(staff.Id.Value, "staffId"),
            Enum.IsDefined(staff.Role) ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.Single(new ModelError.InvalidValue(), "role"))),
        Actor.Client client => Validation.Identifier(client.Id.Value, "clientId")
    };
}
