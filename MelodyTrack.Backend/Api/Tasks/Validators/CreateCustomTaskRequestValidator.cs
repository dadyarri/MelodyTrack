using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Tasks.Requests;

namespace MelodyTrack.Backend.Api.Tasks.Validators;

public class CreateCustomTaskRequestValidator : Validator<CreateCustomTaskRequest>
{
    public CreateCustomTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Укажите заголовок задачи.")
            .MaximumLength(200)
            .WithMessage("Заголовок задачи не должен быть длиннее 200 символов.");

        RuleFor(x => x.MessageText)
            .NotEmpty()
            .WithMessage("Укажите текст задачи.")
            .MaximumLength(2000)
            .WithMessage("Текст задачи не должен быть длиннее 2000 символов.");

        RuleFor(x => x.RecipientName)
            .NotEmpty()
            .When(x => !x.ClientId.HasValue)
            .WithMessage("Укажите имя получателя для задачи без клиента.")
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.RecipientName))
            .WithMessage("Имя получателя не должно быть длиннее 200 символов.");
    }
}
