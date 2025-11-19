using FastEndpoints;
using FluentValidation;
using MelodyTrack.Common.Api.Expenses.Requests;

namespace MelodyTrack.Backend.Api.Expenses.Validators;

public class CreateExpenseRequestValidator : Validator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(e => e.Amount)
            .GreaterThan(0)
            .WithMessage("Сумма расхода должна быть больше нуля");

        RuleFor(e => e.Description)
            .NotEmpty()
            .WithMessage("Описание расхода не должно быть пустым");
    }
}