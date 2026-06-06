using FastEndpoints;
using FluentValidation;
using MelodyTrack.Backend.Api.Payments.Requests;

namespace MelodyTrack.Backend.Api.Payments.Validators;

public class UpdatePaymentRequestValidator : Validator<UpdatePaymentRequest>
{
    public UpdatePaymentRequestValidator()
    {
        RuleFor(e => e.Amount)
            .GreaterThan(0)
            .WithMessage("Сумма платежа должна быть больше нуля");
    }
}
