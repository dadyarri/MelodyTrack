using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.ClientPortal.Requests;

namespace MelodyTrack.Backend.Api.ClientPortal.Validators;

public class AuthenticateSavedClientPortalIdentityRequestValidator : Validator<AuthenticateSavedClientPortalIdentityRequest>
{
    public AuthenticateSavedClientPortalIdentityRequestValidator()
    {
        RuleFor(item => item.Reference)
            .NotEmpty()
            .WithMessage("Сохраненный профиль недействителен");

        RuleFor(item => item.Pin)
            .Matches(@"^\d{4}$")
            .WithMessage("PIN-код должен состоять из 4 цифр");
    }
}
