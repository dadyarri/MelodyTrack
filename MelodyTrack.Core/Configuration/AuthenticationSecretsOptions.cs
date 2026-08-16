using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class AuthenticationSecretsOptions
{
    public const string SectionName = "AuthenticationSecrets";

    [Required]
    [MinLength(32)]
    public required string JwtSigningKey { get; init; }

    [MinLength(32)]
    public string? JwtSigningPrivateKey { get; init; }

    [MinLength(32)]
    public string? PasswordPepper { get; init; }

    [MinLength(32)]
    public string? PortalPinPepper { get; init; }

    [MinLength(32)]
    public string? RefreshTokenHashKey { get; init; }

    [MinLength(32)]
    public string? CsrfSigningKey { get; init; }
}
