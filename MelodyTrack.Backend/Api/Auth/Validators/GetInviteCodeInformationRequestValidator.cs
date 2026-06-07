using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class GetInviteCodeInformationRequestValidator : Validator<GetInviteCodeInformationRequest>
{
    public GetInviteCodeInformationRequestValidator()
    {
        RuleFor(e => e.InviteCode)
            .NotEmpty()
            .WithMessage("Код приглашения обязателен")
            .MaximumLength(128)
            .WithMessage("Код приглашения слишком длинный");
    }
}
