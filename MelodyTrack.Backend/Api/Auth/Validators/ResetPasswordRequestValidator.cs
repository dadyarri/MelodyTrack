using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class ResetPasswordRequestValidator : Validator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator(IWebHostEnvironment env)
    {
        var commonPasswordsPath = AuthValidationRules.ResolveCommonPasswordsPath(env.ContentRootPath);

        RuleFor(e => e.Token)
            .NotEmpty()
            .WithMessage("Токен восстановления обязателен")
            .MaximumLength(512)
            .WithMessage("Токен восстановления слишком длинный");

        RuleFor(e => e.NewPassword)
            .ApplyRequiredPasswordRules("Пароль", commonPasswordsPath);

        RuleFor(e => e.Otp)
            .Matches("^\\d{6}$")
            .WithMessage("Код 2FA должен содержать 6 цифр")
            .When(e => !string.IsNullOrWhiteSpace(e.Otp));

        RuleFor(e => e.RecoveryCode)
            .MaximumLength(64)
            .WithMessage("Код восстановления слишком длинный")
            .When(e => !string.IsNullOrWhiteSpace(e.RecoveryCode));

        RuleFor(e => e)
            .Must(e => string.IsNullOrWhiteSpace(e.Otp) || string.IsNullOrWhiteSpace(e.RecoveryCode))
            .WithMessage("Используйте либо код 2FA, либо код восстановления");
    }
}
