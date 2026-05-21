using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Users.Requests;

namespace MelodyTrack.Backend.Api.Users.Validators;

public class UpdateUserAvailabilityRequestValidator : Validator<UpdateUserAvailabilityRequest>
{
    private static readonly string[] AllowedDays =
    [
        "monday",
        "tuesday",
        "wednesday",
        "thursday",
        "friday",
        "saturday",
        "sunday"
    ];

    public UpdateUserAvailabilityRequestValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор пользователя не может быть пустым.");

        RuleFor(x => x.WorkingHours)
            .NotNull()
            .Must(items => items.Count == 7)
            .WithMessage("Нужно указать рабочие часы для всех дней недели.");

        RuleForEach(x => x.WorkingHours)
            .ChildRules(day =>
            {
                day.RuleFor(x => x.DayOfWeek)
                    .Must(value => AllowedDays.Contains(value.Trim().ToLowerInvariant()))
                    .WithMessage("Укажите корректный день недели.");

                day.RuleFor(x => x.StartTime)
                    .NotEmpty()
                    .When(x => x.IsWorkingDay)
                    .WithMessage("Укажите время начала рабочего дня.");

                day.RuleFor(x => x.EndTime)
                    .NotEmpty()
                    .When(x => x.IsWorkingDay)
                    .WithMessage("Укажите время окончания рабочего дня.");

                day.RuleFor(x => x)
                    .Must(item => !item.IsWorkingDay || IsValidTimeRange(item.StartTime, item.EndTime))
                    .WithMessage("Время работы указано некорректно.");
            });

        RuleFor(x => x.WorkingHours)
            .Must(items => items
                .Select(item => item.DayOfWeek.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count() == 7)
            .When(x => x.WorkingHours is not null)
            .WithMessage("Каждый день недели должен быть указан ровно один раз.");

        RuleForEach(x => x.Vacations)
            .ChildRules(vacation =>
            {
                vacation.RuleFor(x => x.EndDate)
                    .GreaterThanOrEqualTo(x => x.StartDate)
                    .WithMessage("Дата окончания отпуска не может быть раньше даты начала.");
            });
    }

    private static bool IsValidTimeRange(string? startTime, string? endTime)
    {
        return TimeOnly.TryParse(startTime, out var start)
               && TimeOnly.TryParse(endTime, out var end)
               && start < end;
    }
}
