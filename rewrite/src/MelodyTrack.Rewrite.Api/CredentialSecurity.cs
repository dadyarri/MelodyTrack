using System.Security.Cryptography;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OtpNet;

namespace MelodyTrack.Rewrite.Api;

public sealed class PasskeyOptions
{
    public const string SectionName = "Authentication:Passkeys";
    public string RelyingPartyId { get; set; } = "localhost";
    public string ServerName { get; set; } = "MelodyTrack";
    public HashSet<string> Origins { get; set; } = [];
}

public sealed class CredentialSecurity(IOptions<PasskeyOptions> options, PersonalDataProtector protection) : ICredentialSecurity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PasswordHasher<object> passwords = new();
    private readonly object passwordContext = new();
    private readonly Fido2 fido = new(new Fido2Configuration
    {
        RPID = options.Value.RelyingPartyId,
        ChallengeSize = 32,
        RPName = options.Value.ServerName,
        Origins = options.Value.Origins
    });

    public string HashPassword(string password) => passwords.HashPassword(passwordContext, password);

    public bool VerifyPassword(string hash, string password)
    {
        try { return passwords.VerifyHashedPassword(passwordContext, hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    public string CreateAuthenticatorSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(32));
    public string ProtectAuthenticator(StaffId staffId, string secret) => protection.Protect(secret, $"auth:totp:{staffId.Value:D}");
    public string UnprotectAuthenticator(StaffId staffId, string protectedSecret) => protection.Unprotect(protectedSecret, $"auth:totp:{staffId.Value:D}");

    public long? VerifyAuthenticator(string secret, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9')) return null;
        byte[]? key = null;
        try
        {
            key = Base32Encoding.ToBytes(secret);
            return new Totp(key).VerifyTotp(now.UtcDateTime, code, out var step, VerificationWindow.RfcSpecifiedNetworkDelay) ? step : null;
        }
        catch (ArgumentException) { return null; }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }

    public string CreatePasskeyRegistration(StaffId staffId, string login, IReadOnlyList<StoredPasskey> existing) =>
        fido.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = staffId.Value.ToByteArray(), Name = login, DisplayName = login },
            ExcludeCredentials = existing.Select(key => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(key.CredentialId))).ToArray(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        }).ToJson();

    public async Task<StoredPasskey?> VerifyPasskeyRegistrationAsync(StaffId staffId, string optionsJson, string responseJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var original = CredentialCreateOptions.FromJson(optionsJson);
            if (!original.User.Id.SequenceEqual(staffId.Value.ToByteArray())) return null;
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(responseJson, JsonOptions);
            if (response is null || response.RawId is not { Length: > 0 } || response.Response is null) return null;
            var result = await fido.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = original,
                // The store enforces global credential-id uniqueness atomically when saving the verified key.
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
            }, cancellationToken);
            return new(WebEncoders.Base64UrlEncode(result.Id), result.PublicKey, result.SignCount, original.User.Id);
        }
        catch (Exception error) when (InvalidWebAuthn(error)) { return null; }
    }

    public string CreatePasskeyAssertion(IReadOnlyList<StoredPasskey> credentials) => fido.GetAssertionOptions(new GetAssertionOptionsParams
    {
        AllowedCredentials = credentials.Select(key => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(key.CredentialId))).ToArray(),
        UserVerification = UserVerificationRequirement.Required
    }).ToJson();

    public PasskeyAssertionIdentity? ReadPasskeyAssertionIdentity(string responseJson)
    {
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson, JsonOptions);
            if (response?.RawId is not { Length: > 0 } || response.Response?.UserHandle is not { Length: > 0 }) return null;
            return new(WebEncoders.Base64UrlEncode(response.RawId), response.Response.UserHandle);
        }
        catch (Exception error) when (InvalidWebAuthn(error)) { return null; }
    }

    public async Task<uint?> VerifyPasskeyAssertionAsync(StaffId staffId, string optionsJson, string responseJson, StoredPasskey credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!credential.UserHandle.SequenceEqual(staffId.Value.ToByteArray())) return null;
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson, JsonOptions);
            if (response is null || response.RawId is not { Length: > 0 } || response.Response is null
                || WebEncoders.Base64UrlEncode(response.RawId) != credential.CredentialId) return null;
            var original = AssertionOptions.FromJson(optionsJson);
            var hasUserHandle = response.Response.UserHandle is { Length: > 0 };
            if (!original.AllowCredentials.Any() && !hasUserHandle
                || hasUserHandle && !response.Response.UserHandle!.SequenceEqual(credential.UserHandle)) return null;
            var result = await fido.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = original,
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = credential.Counter,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                    args.UserHandle.SequenceEqual(credential.UserHandle) && args.CredentialId.SequenceEqual(response.RawId))
            }, cancellationToken);
            return result.SignCount;
        }
        catch (Exception error) when (InvalidWebAuthn(error)) { return null; }
    }

    private static bool InvalidWebAuthn(Exception error) => error is Fido2VerificationException or JsonException
        or FormatException or ArgumentException or InvalidOperationException or CryptographicException;
}
