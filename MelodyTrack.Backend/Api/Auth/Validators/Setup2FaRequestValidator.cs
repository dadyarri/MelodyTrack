using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class Setup2FaRequestValidator : Validator<Setup2FaRequest>
{
    public Setup2FaRequestValidator()
    {
        RuleFor(e => e.Password)
            .NotEmpty()
            .WithMessage("Пароль обязателен")
            .MaximumLength(256)
            .WithMessage("Пароль слишком длинный");
    }
}
