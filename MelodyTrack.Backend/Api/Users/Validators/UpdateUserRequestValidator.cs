using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Users.Requests;

namespace MelodyTrack.Backend.Api.Users.Validators;

public class UpdateUserRequestValidator : Validator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор пользователя не может быть пустым.");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithMessage("Укажите имя пользователя.")
            .MaximumLength(128)
            .WithMessage("Имя пользователя не должно быть длиннее 128 символов.");

        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithMessage("Укажите фамилию пользователя.")
            .MaximumLength(128)
            .WithMessage("Фамилия пользователя не должна быть длиннее 128 символов.");

        RuleFor(x => x.Phone)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Телефон указан некорректно.");

        RuleFor(x => x.Telegram)
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Telegram))
            .WithMessage("Telegram указан некорректно.");

        RuleFor(x => x.Vk)
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Vk))
            .WithMessage("VK указан некорректно.");
    }
}
