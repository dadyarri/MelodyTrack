using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Validators;

public class CreateCourseEnrollmentRequestValidator : Validator<CreateCourseEnrollmentRequest>
{
    public CreateCourseEnrollmentRequestValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty()
            .WithMessage("Укажите клиента.");

        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithMessage("Укажите курс.");
    }
}
