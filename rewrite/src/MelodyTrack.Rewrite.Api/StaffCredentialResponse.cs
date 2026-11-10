using MelodyTrack.Rewrite.Application;

namespace MelodyTrack.Rewrite.Api;

/// <summary>Сохраняет HTTP-контракт независимо от внутренних вариантов результата входа.</summary>
public sealed record StaffCredentialResponse(string? AccessToken = null, string? RefreshToken = null,
    string? Challenge = null, string? Factor = null, string? OptionsJson = null, string? AuthenticatorSecret = null,
    Guid? StaffId = null, int ExpiresIn = 300)
{
    public override string ToString() => nameof(StaffCredentialResponse);

    public static StaffCredentialResponse From(StaffCredentialResult result) => result switch
    {
        StaffCredentialResult.Tokens tokens => new(tokens.AccessToken, tokens.RefreshToken, StaffId: tokens.StaffId),
        StaffCredentialResult.LinkIssued link => new(Challenge: link.Challenge),
        StaffCredentialResult.RegistrationOptions registration => new(Challenge: registration.Challenge,
            OptionsJson: registration.OptionsJson, AuthenticatorSecret: registration.AuthenticatorSecret),
        StaffCredentialResult.PasskeyOptions passkey => new(Challenge: passkey.Challenge, OptionsJson: passkey.OptionsJson),
        StaffCredentialResult.AuthenticatorSetup authenticator => new(Challenge: authenticator.Challenge,
            AuthenticatorSecret: authenticator.AuthenticatorSecret),
        StaffCredentialResult.FactorRequired factor => new(Challenge: factor.Challenge, Factor: factor.Factor, OptionsJson: factor.OptionsJson),
        StaffCredentialResult.Completed => new()
    };
}
