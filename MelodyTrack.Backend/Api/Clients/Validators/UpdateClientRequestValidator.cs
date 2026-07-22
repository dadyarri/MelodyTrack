using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Clients.Requests;

namespace MelodyTrack.Backend.Api.Clients.Validators;

public class UpdateClientRequestValidator : Validator<UpdateClientRequest>
{
    public UpdateClientRequestValidator()
    {
        RuleForEach(item => item.Vacations)
            .Must(item => item.StartDate <= item.EndDate)
            .WithMessage("Дата окончания отсутствия не может быть раньше даты начала");
    }
}
