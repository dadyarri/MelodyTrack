using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class Recover2FaRequestValidator : Validator<Recover2FaRequest>
{
    public Recover2FaRequestValidator()
    {
        RuleFor(e => e.Email)
            .NotEmpty()
            .WithMessage("Email обязателен")
            .EmailAddress()
            .WithMessage("Невалидный email");

        RuleFor(e => e.RecoveryCode)
            .NotEmpty()
            .WithMessage("Код восстановления обязателен")
            .MaximumLength(64)
            .WithMessage("Код восстановления слишком длинный");
    }
}
