using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.ClientPortal.Requests;

namespace MelodyTrack.Backend.Api.ClientPortal.Validators;

public class GetClientPortalLinkStatusRequestValidator : Validator<GetClientPortalLinkStatusRequest>
{
    public GetClientPortalLinkStatusRequestValidator()
    {
        RuleFor(item => item.Token)
            .NotEmpty()
            .WithMessage("Ссылка входа недействительна");
    }
}
