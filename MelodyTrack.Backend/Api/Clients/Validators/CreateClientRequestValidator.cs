using FastEndpoints;
using FluentValidation;
using MelodyTrack.Common.Api.Clients.Requests;

namespace MelodyTrack.Backend.Api.Clients.Validators;

public class CreateClientRequestValidator : Validator<CreateClientRequest>
{
    public CreateClientRequestValidator()
    {
        RuleFor(e => e.FirstName)
            .NotEmpty()
            .WithMessage("Имя обязательно");

        RuleFor(e => e.LastName)
            .NotEmpty()
            .WithMessage("Фамилия обязательна");
    }
}