using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Expenses.Requests;

namespace MelodyTrack.Backend.Api.Expenses.Validators;

public class UpdateExpenseRequestValidator : Validator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(e => e.Amount)
            .GreaterThanOrEqualTo(0.01m)
            .WithMessage("Сумма расхода должна быть больше нуля");

        RuleFor(e => e.Amount)
            .Must(amount => decimal.Round(amount, 2) == amount)
            .WithMessage("Сумма расхода может содержать не более двух знаков после запятой");

        RuleFor(e => e.Date)
            .NotEqual(default(DateTime))
            .WithMessage("Укажите дату расхода");

        RuleFor(e => e.Description)
            .NotEmpty()
            .WithMessage("Описание расхода не должно быть пустым");
    }
}
