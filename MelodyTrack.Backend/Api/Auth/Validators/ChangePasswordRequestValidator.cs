using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class ChangePasswordRequestValidator : Validator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(IWebHostEnvironment env)
    {
        var commonPasswordsPath = AuthValidationRules.ResolveCommonPasswordsPath(env.ContentRootPath);

        RuleFor(e => e.CurrentPassword)
            .NotEmpty()
            .WithMessage("Текущий пароль обязателен")
            .MaximumLength(256)
            .WithMessage("Пароль слишком длинный");

        RuleFor(e => e.NewPassword)
            .ApplyRequiredPasswordRules("Новый пароль", commonPasswordsPath);
    }
}
