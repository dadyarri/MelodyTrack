using FluentValidation;

namespace MelodyTrack.Web.Utils;

public class FluentValueValidator<T> : AbstractValidator<T>
{
    public FluentValueValidator(Action<IRuleBuilderInitial<T, T>> rule)
    {
        rule(RuleFor(x => x));
    }

    public Func<T, IEnumerable<string>> Validation => ValidateValue;

    private IEnumerable<string> ValidateValue(T arg)
    {
        var result = Validate(arg);
        if (result.IsValid)
        {
            return [];
        }
        return result.Errors.Select(e => e.ErrorMessage);
    }
}