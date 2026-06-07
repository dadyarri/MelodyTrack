using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class RegisterRequestValidator : Validator<RegisterRequest>
{
    public RegisterRequestValidator(IWebHostEnvironment env)
    {
        var commonPasswordsPath = AuthValidationRules.ResolveCommonPasswordsPath(env.ContentRootPath);

        RuleFor(e => e.Email)
            .NotEmpty()
            .WithMessage("Email обязателен")
            .EmailAddress()
            .WithMessage("Невалидный email");

        RuleFor(e => e.Password)
            .ApplyRequiredPasswordRules("Пароль", commonPasswordsPath);
    }
}
