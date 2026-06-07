using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Courses.Requests;

namespace MelodyTrack.Backend.Api.Courses.Validators;

public class UpdateCourseRequestValidator : Validator<UpdateCourseRequest>
{
    public UpdateCourseRequestValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => id != Ulid.Empty)
            .WithMessage("Идентификатор курса не может быть пустым.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Укажите название курса.")
            .MaximumLength(200)
            .WithMessage("Название курса не должно быть длиннее 200 символов.");

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Описание курса не должно быть длиннее 2000 символов.");

        RuleForEach(x => x.Blocks)
            .ChildRules(block =>
            {
                block.RuleFor(x => x.Title)
                    .NotEmpty()
                    .WithMessage("Укажите название блока.")
                    .MaximumLength(200)
                    .WithMessage("Название блока не должно быть длиннее 200 символов.");

                block.RuleFor(x => x.Description)
                    .MaximumLength(2000)
                    .When(x => !string.IsNullOrWhiteSpace(x.Description))
                    .WithMessage("Описание блока не должно быть длиннее 2000 символов.");

                block.RuleFor(x => x.Order)
                    .GreaterThan(0)
                    .WithMessage("Порядок блока должен быть больше нуля.");

                block.RuleForEach(x => x.Branches)
                    .ChildRules(branch =>
                    {
                        branch.RuleFor(x => x.Title)
                            .NotEmpty()
                            .WithMessage("Укажите название ветки.")
                            .MaximumLength(200)
                            .WithMessage("Название ветки не должно быть длиннее 200 символов.");

                        branch.RuleFor(x => x.Description)
                            .MaximumLength(2000)
                            .When(x => !string.IsNullOrWhiteSpace(x.Description))
                            .WithMessage("Описание ветки не должно быть длиннее 2000 символов.");

                        branch.RuleFor(x => x.Order)
                            .GreaterThan(0)
                            .WithMessage("Порядок ветки должен быть больше нуля.");

                        branch.RuleForEach(x => x.Themes)
                            .ChildRules(theme =>
                            {
                                theme.RuleFor(x => x.Key)
                                    .NotEmpty()
                                    .WithMessage("Укажите ключ темы.")
                                    .MaximumLength(100)
                                    .WithMessage("Ключ темы не должен быть длиннее 100 символов.");

                                theme.RuleFor(x => x.Title)
                                    .NotEmpty()
                                    .WithMessage("Укажите название темы.")
                                    .MaximumLength(200)
                                    .WithMessage("Название темы не должно быть длиннее 200 символов.");

                                theme.RuleFor(x => x.Description)
                                    .MaximumLength(4000)
                                    .When(x => !string.IsNullOrWhiteSpace(x.Description))
                                    .WithMessage("Описание темы не должно быть длиннее 4000 символов.");

                                theme.RuleFor(x => x.Order)
                                    .GreaterThan(0)
                                    .WithMessage("Порядок темы должен быть больше нуля.");

                                theme.RuleFor(x => x.UnlockCostPoints)
                                    .GreaterThanOrEqualTo(0)
                                    .WithMessage("Стоимость разблокировки не может быть отрицательной.");

                                theme.RuleFor(x => x.EvolutionPointsReward)
                                    .GreaterThanOrEqualTo(0)
                                    .WithMessage("Очки эволюции не могут быть меньше нуля.");

                                theme.RuleFor(x => x.ExperiencePointsReward)
                                    .GreaterThanOrEqualTo(0)
                                    .WithMessage("Очки опыта не могут быть меньше нуля.");
                            });
                    });
            });

        RuleFor(x => x)
            .Custom((request, context) =>
            {
                CourseStructureValidation.ValidateBlocks(request.Blocks, context.AddFailure);
            });
    }
}
