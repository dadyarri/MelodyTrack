using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Schedule.Requests;

namespace MelodyTrack.Backend.Api.Schedule.Validators;

public class CreateAppointmentRequestValidator : Validator<CreateAppointmentRequest>
{
    public CreateAppointmentRequestValidator()
    {
        RuleFor(x => x.ClientId)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор клиента не может быть пустым.");

        RuleFor(x => x.ServiceId)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор услуги не может быть пустым.");

        RuleFor(x => x.Timezone)
            .NotEmpty()
            .WithMessage("Нужно указать таймзону.");

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.PatternEndDate)
            .When(x => x.PatternEndDate.HasValue)
            .WithMessage("Дата начала не может быть позже даты окончания шаблона.");

        RuleFor(x => x.RecurrencePattern)
            .NotNull()
            .When(x => x.RecurrenceTypeId.HasValue) // If RecurrenceTypeId is set
            .WithMessage("Шаблон повторения должен быть указан для повторяющейся записи.");

        RuleFor(x => x.PatternEndDate)
            .Null()
            .When(x => !x.RecurrenceTypeId.HasValue)
            .WithMessage("Дата окончания шаблона должна быть пустой для однократной записи.");

        RuleFor(x => x.RecurrencePattern)
            .Null()
            .When(x => !x.RecurrenceTypeId.HasValue)
            .WithMessage("Шаблон повторения должен быть пустым для однократной записи.");

        RuleFor(x => x.LessonNotes)
            .MaximumLength(4000)
            .WithMessage("Заметки к уроку не должны быть длиннее 4000 символов.");
    }
}
