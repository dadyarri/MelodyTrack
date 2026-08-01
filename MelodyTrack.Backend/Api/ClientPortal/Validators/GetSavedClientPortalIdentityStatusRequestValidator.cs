using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.ClientPortal.Requests;

namespace MelodyTrack.Backend.Api.ClientPortal.Validators;

public class GetSavedClientPortalIdentityStatusRequestValidator : Validator<GetSavedClientPortalIdentityStatusRequest>
{
    public GetSavedClientPortalIdentityStatusRequestValidator()
    {
        RuleFor(item => item.Reference)
            .NotEmpty()
            .WithMessage("Сохраненный профиль недействителен");
    }
}
