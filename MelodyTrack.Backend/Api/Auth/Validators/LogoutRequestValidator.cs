using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class LogoutRequestValidator : Validator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(e => e.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token обязателен")
            .MaximumLength(512)
            .WithMessage("Refresh token слишком длинный");
    }
}
