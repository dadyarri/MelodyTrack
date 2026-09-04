using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class AuthenticationSecretsOptions
{
    public const string SectionName = "AuthenticationSecrets";

    [Required]
    public required string JwtSigningPrivateKey { get; init; }

    [Required]
    public required string PasswordPepper { get; init; }

    [Required]
    public required string PortalPinPepper { get; init; }

    [Required]
    public required string RefreshTokenHashKey { get; init; }

    [Required]
    public required string CsrfSigningKey { get; init; }
}
