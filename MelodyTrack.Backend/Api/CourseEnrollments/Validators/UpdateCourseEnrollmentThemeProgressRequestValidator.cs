using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Validators;

public class UpdateCourseEnrollmentThemeProgressRequestValidator : Validator<UpdateCourseEnrollmentThemeProgressRequest>
{
    public UpdateCourseEnrollmentThemeProgressRequestValidator()
    {
        RuleFor(item => item.Action)
            .NotEmpty()
            .WithMessage("Действие обязательно.")
            .Must(value => CourseEnrollmentThemeProgressActionExtensions.TryParseApiKey(value, out _))
            .WithMessage("Некорректное действие прогресса.");
    }
}
