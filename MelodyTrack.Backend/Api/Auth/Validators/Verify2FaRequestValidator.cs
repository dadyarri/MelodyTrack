using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Auth.Requests;

namespace MelodyTrack.Backend.Api.Auth.Validators;

public class Verify2FaRequestValidator : Validator<Verify2FaRequest>
{
    public Verify2FaRequestValidator()
    {
        RuleFor(e => e.Email)
            .EmailAddress()
            .WithMessage("Невалидный email")
            .When(e => !string.IsNullOrWhiteSpace(e.Email));

        RuleFor(e => e.Otp)
            .NotEmpty()
            .WithMessage("Код 2FA обязателен")
            .Matches("^\\d{6}$")
            .WithMessage("Код 2FA должен содержать 6 цифр");

        RuleFor(e => e.OtpSecret)
            .NotEmpty()
            .WithMessage("Секрет 2FA обязателен")
            .MaximumLength(256)
            .WithMessage("Секрет 2FA слишком длинный");
    }
}
