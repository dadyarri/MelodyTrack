using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.ClientSources.Requests;

namespace MelodyTrack.Backend.Api.ClientSources.Validators;

public class CreateClientSourceRequestValidator : Validator<CreateClientSourceRequest>
{
    public CreateClientSourceRequestValidator()
    {
        RuleFor(e => e.Name)
            .NotEmpty()
            .WithMessage("Название источника обязательно")
            .MaximumLength(200)
            .WithMessage("Название источника должно быть не длиннее 200 символов");
    }
}
