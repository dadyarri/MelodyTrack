using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.ClientPortal.Requests;

namespace MelodyTrack.Backend.Api.ClientPortal.Validators;

public class AuthenticateClientPortalLinkRequestValidator : Validator<AuthenticateClientPortalLinkRequest>
{
    public AuthenticateClientPortalLinkRequestValidator()
    {
        RuleFor(item => item.Token)
            .NotEmpty()
            .WithMessage("Ссылка входа недействительна");

        RuleFor(item => item.Pin)
            .Matches(@"^\d{4}$")
            .WithMessage("PIN-код должен состоять из 4 цифр");

        RuleFor(item => item.PinConfirmation)
            .Matches(@"^\d{4}$")
            .When(item => item.PinConfirmation is not null)
            .WithMessage("Подтверждение PIN-кода должно состоять из 4 цифр");
    }
}
