using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class CredentialSecurityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId Staff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly StaffId OtherStaff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private const string Password = "Synthetic passphrase 27!";
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void HashPassword_RepeatedPassword_UsesIndependentSaltAndVerifiesOnlyMatchingPassword()
    {
        using var protection = Protection();
        var security = Security(protection);

        var first = security.HashPassword(Password);
        var second = security.HashPassword(Password);

        Assert.NotEqual(first, second);
        Assert.NotEqual(Password, first);
        Assert.True(security.VerifyPassword(first, Password));
        Assert.True(security.VerifyPassword(second, Password));
        Assert.False(security.VerifyPassword(first, "Different synthetic passphrase"));
    }

    [Fact]
    public void CreateAuthenticatorSecret_TwoRequests_UsesIndependentSecrets()
    {
        using var protection = Protection();
        var security = Security(protection);

        var first = security.CreateAuthenticatorSecret();
        var second = security.CreateAuthenticatorSecret();

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void VerifyAuthenticator_Rfc6238Sha1Vector_ReturnsMatchingTimeStep()
    {
        using var protection = Protection();
        var security = Security(protection);

        var step = security.VerifyAuthenticator(RfcSecret, "287082", DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Equal(1L, step);
    }

    [Fact]
    public void VerifyAuthenticator_CodeOutsideWindow_RejectsCode()
    {
        using var protection = Protection();
        var security = Security(protection);

        var step = security.VerifyAuthenticator(RfcSecret, "287082", DateTimeOffset.FromUnixTimeSeconds(149));

        Assert.Null(step);
    }

    [Theory]
    [InlineData("")]
    [InlineData("28708")]
    [InlineData("0287082")]
    [InlineData("abcdef")]
    public void VerifyAuthenticator_MalformedCode_RejectsCode(string code)
    {
        using var protection = Protection();
        var security = Security(protection);

        var step = security.VerifyAuthenticator(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Null(step);
    }

    [Fact]
    public void ProtectAuthenticator_DifferentOwner_CannotReadSecret()
    {
        using var protection = Protection();
        var security = Security(protection);
        var encrypted = security.ProtectAuthenticator(Staff, RfcSecret);

        var restored = security.UnprotectAuthenticator(Staff, encrypted);

        Assert.Equal(RfcSecret, restored);
        Assert.DoesNotContain(RfcSecret, encrypted);
        Assert.ThrowsAny<CryptographicException>(() => security.UnprotectAuthenticator(OtherStaff, encrypted));
    }

    [Fact]
    public void CreatePasskeyRegistration_RepeatedRequests_UsesFreshChallengesAndBindsOwner()
    {
        using var protection = Protection();
        var security = Security(protection);

        using var first = JsonDocument.Parse(security.CreatePasskeyRegistration(Staff, "synthetic-user", []));
        using var second = JsonDocument.Parse(security.CreatePasskeyRegistration(Staff, "synthetic-user", []));

        Assert.NotEqual(first.RootElement.GetProperty("challenge").GetString(), second.RootElement.GetProperty("challenge").GetString());
        Assert.Equal("localhost", first.RootElement.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("synthetic-user", first.RootElement.GetProperty("user").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(first.RootElement.GetProperty("user").GetProperty("id").GetString()));
    }

    [Fact]
    public void CreatePasskeyRegistration_NewCredential_RequiresDiscoverableUserVerifiedCredential()
    {
        using var protection = Protection();
        var security = Security(protection);

        using var options = JsonDocument.Parse(security.CreatePasskeyRegistration(Staff, "discoverable-user", []));

        var selection = options.RootElement.GetProperty("authenticatorSelection");
        Assert.Equal("required", selection.GetProperty("residentKey").GetString());
        Assert.True(selection.GetProperty("requireResidentKey").GetBoolean());
        Assert.Equal("required", selection.GetProperty("userVerification").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("invalid-json")]
    [InlineData("{\"rawId\":\"AQID\",\"response\":{}}")]
    [InlineData("{\"rawId\":\"AQID\",\"response\":{\"userHandle\":null}}")]
    public void ReadPasskeyAssertionIdentity_MalformedOrMissingHandle_ReturnsNull(string response)
    {
        using var protection = Protection();
        var security = Security(protection);

        var identity = security.ReadPasskeyAssertionIdentity(response);

        Assert.Null(identity);
    }

    [Fact]
    public void ReadPasskeyAssertionIdentity_AssertionWithHandle_ReturnsUnverifiedSelectorWithoutDiagnosticValues()
    {
        using var protection = Protection();
        var security = Security(protection);
        using var authenticator = new WebAuthnTestAuthenticator();
        var registration = security.CreatePasskeyRegistration(Staff, "discoverable-user", []);
        authenticator.Register(registration);
        var options = security.CreatePasskeyAssertion([]);

        var identity = security.ReadPasskeyAssertionIdentity(authenticator.Assert(options));

        Assert.NotNull(identity);
        Assert.Equal(authenticator.CredentialId, identity.CredentialId);
        Assert.Equal(Staff.Value.ToByteArray(), identity.UserHandle);
        Assert.Equal(nameof(PasskeyAssertionIdentity), identity.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatched")]
    public async Task VerifyPasskeyAssertion_DiscoveryWithoutMatchingHandle_RejectsOtherwiseValidSignature(string handle)
    {
        using var protection = Protection();
        var security = Security(protection);
        using var authenticator = new WebAuthnTestAuthenticator();
        var registration = security.CreatePasskeyRegistration(Staff, "discoverable-user", []);
        var credential = await security.VerifyPasskeyRegistrationAsync(Staff, registration, authenticator.Register(registration), Token);
        Assert.NotNull(credential);
        var options = security.CreatePasskeyAssertion([]);
        var assertion = JsonNode.Parse(authenticator.Assert(options))!;
        if (handle == "missing") assertion["response"]!.AsObject().Remove("userHandle");
        else assertion["response"]!["userHandle"] = "AQID";

        var result = await security.VerifyPasskeyAssertionAsync(Staff, options, assertion.ToJsonString(), credential, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyPasskeyRegistration_InvalidResponse_DoesNotCreateCredential()
    {
        using var protection = Protection();
        var security = Security(protection);
        var options = security.CreatePasskeyRegistration(Staff, "synthetic-user", []);

        var result = await security.VerifyPasskeyRegistrationAsync(Staff, options, "{}", Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyPasskeyAssertion_SignedSoftwareAuthenticator_VerifiesRegistrationAndAssertion()
    {
        using var protection = Protection();
        var security = Security(protection);
        using var authenticator = new WebAuthnTestAuthenticator();
        var registration = security.CreatePasskeyRegistration(Staff, "synthetic-user", []);

        var credential = await security.VerifyPasskeyRegistrationAsync(Staff, registration, authenticator.Register(registration), Token);

        Assert.NotNull(credential);
        Assert.Equal(authenticator.CredentialId, credential.CredentialId);
        var assertion = security.CreatePasskeyAssertion([credential]);
        var counter = await security.VerifyPasskeyAssertionAsync(Staff, assertion, authenticator.Assert(assertion), credential, Token);
        Assert.Equal(1u, counter);
    }

    [Fact]
    public async Task VerifyPasskeyAssertion_DifferentChallenge_RejectsValidSignatureForAnotherCeremony()
    {
        using var protection = Protection();
        var security = Security(protection);
        using var authenticator = new WebAuthnTestAuthenticator();
        var registration = security.CreatePasskeyRegistration(Staff, "synthetic-user", []);
        var credential = await security.VerifyPasskeyRegistrationAsync(Staff, registration, authenticator.Register(registration), Token);
        Assert.NotNull(credential);
        var expected = security.CreatePasskeyAssertion([credential]);
        var different = security.CreatePasskeyAssertion([credential]);

        var result = await security.VerifyPasskeyAssertionAsync(Staff, expected, authenticator.Assert(different), credential, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyPasskeyRegistration_UntrustedOrigin_RejectsResponse()
    {
        using var protection = Protection();
        var security = Security(protection);
        using var authenticator = new WebAuthnTestAuthenticator();
        var registration = security.CreatePasskeyRegistration(Staff, "synthetic-user", []);

        var credential = await security.VerifyPasskeyRegistrationAsync(Staff, registration,
            authenticator.Register(registration, "https://untrusted.invalid"), Token);

        Assert.Null(credential);
    }

    [Fact]
    public async Task VerifyPasskeyAssertion_InvalidResponse_DoesNotAuthenticate()
    {
        using var protection = Protection();
        var security = Security(protection);
        var credential = new StoredPasskey("AQIDBA", [1, 2, 3, 4], 0, Staff.Value.ToByteArray());
        var options = security.CreatePasskeyAssertion([credential]);

        var result = await security.VerifyPasskeyAssertionAsync(Staff, options, "{}", credential, Token);

        Assert.Null(result);
    }

    private static CredentialSecurity Security(PersonalDataProtector protection) => new(Options.Create(new PasskeyOptions
    {
        RelyingPartyId = "localhost",
        ServerName = "Synthetic school",
        Origins = ["https://localhost"]
    }), protection);

    private static PersonalDataProtector Protection() => new("test", new Dictionary<string, string>
    {
        ["test"] = Convert.ToBase64String(Enumerable.Repeat((byte)23, 32).ToArray())
    });
}
