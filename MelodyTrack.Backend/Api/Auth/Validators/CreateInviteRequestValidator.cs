using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class CreateInviteRequestValidator : Validator<CreateInviteRequest>
{
    public CreateInviteRequestValidator()
    {
        RuleFor(e => e.Role)
            .Must(role => role != Ulid.Empty)
            .WithMessage("Роль обязательна");

        RuleFor(e => e.Email)
            .EmailAddress()
            .WithMessage("Невалидный email")
            .When(e => !string.IsNullOrWhiteSpace(e.Email));
    }
}
