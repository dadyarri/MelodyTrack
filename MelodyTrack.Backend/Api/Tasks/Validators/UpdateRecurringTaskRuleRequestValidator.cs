using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Tasks.Requests;

namespace MelodyTrack.Backend.Api.Tasks.Validators;

public class UpdateRecurringTaskRuleRequestValidator : Validator<UpdateRecurringTaskRuleRequest>
{
    public UpdateRecurringTaskRuleRequestValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор правила не может быть пустым.");

        RuleFor(x => x.MessageTemplate)
            .NotEmpty()
            .WithMessage("Укажите текст шаблона.")
            .MaximumLength(1000)
            .WithMessage("Текст шаблона не должен быть длиннее 1000 символов.");

        RuleFor(x => x.OffsetMinutes)
            .GreaterThan(0)
            .When(x => x.OffsetMinutes.HasValue)
            .WithMessage("Смещение должно быть больше нуля.");

        RuleFor(x => x.CooldownDays)
            .GreaterThan(0)
            .When(x => x.CooldownDays.HasValue)
            .WithMessage("Период повтора должен быть больше нуля.");
    }
}
