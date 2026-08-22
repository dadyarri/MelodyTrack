using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Validation;

public interface IValidatableRequest : IValidatableObject
{
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext) =>
        RequestValidation.Validate(this, validationContext);
}
