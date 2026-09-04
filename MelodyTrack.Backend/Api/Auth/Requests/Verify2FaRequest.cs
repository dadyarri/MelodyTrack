using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class Verify2FaRequest : IValidatableRequest
{
    public string? Email { get; set; }
    public required string Otp { get; set; }
    public required string OtpSecret { get; set; }
}