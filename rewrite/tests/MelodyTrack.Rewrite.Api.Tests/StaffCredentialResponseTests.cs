using System.Text.Json;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class StaffCredentialResponseTests
{
    [Theory]
    [InlineData("tokens", "{\"accessToken\":\"test-access\",\"refreshToken\":\"test-refresh\",\"staffId\":\"019a2c63-8c00-7000-8000-000000000001\"}")]
    [InlineData("link", "{\"challenge\":\"test-link\"}")]
    [InlineData("registration", "{\"challenge\":\"test-challenge\",\"optionsJson\":\"{}\"}")]
    [InlineData("registration-totp", "{\"challenge\":\"test-challenge\",\"optionsJson\":\"{}\",\"authenticatorSecret\":\"test-totp\"}")]
    [InlineData("passkey", "{\"challenge\":\"test-challenge\",\"optionsJson\":\"{}\"}")]
    [InlineData("authenticator", "{\"challenge\":\"test-challenge\",\"authenticatorSecret\":\"test-totp\"}")]
    [InlineData("factor-totp", "{\"challenge\":\"test-challenge\",\"factor\":\"authenticator\"}")]
    [InlineData("factor-passkey", "{\"challenge\":\"test-challenge\",\"factor\":\"authenticatorOrPasskey\",\"optionsJson\":\"{}\"}")]
    [InlineData("completed", "{}")]
    public void From_CredentialOutcome_PreservesFlatJsonContract(string scenario, string populatedFields)
    {
        var result = CreateResult(scenario);
        using var expected = JsonDocument.Parse(populatedFields);

        var response = StaffCredentialResponse.From(result);
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(8, actual.RootElement.EnumerateObject().Count());
        Assert.Equal(300, actual.RootElement.GetProperty("expiresIn").GetInt32());
        foreach (var field in expected.RootElement.EnumerateObject())
            Assert.Equal(field.Value.GetString(), actual.RootElement.GetProperty(field.Name).GetString());
        foreach (var field in actual.RootElement.EnumerateObject())
            if (field.Name != "expiresIn" && !expected.RootElement.TryGetProperty(field.Name, out _))
                Assert.Equal(JsonValueKind.Null, field.Value.ValueKind);
    }

    [Theory]
    [InlineData("tokens")]
    [InlineData("link")]
    [InlineData("registration")]
    [InlineData("registration-totp")]
    [InlineData("passkey")]
    [InlineData("authenticator")]
    [InlineData("factor-totp")]
    [InlineData("factor-passkey")]
    [InlineData("completed")]
    public void ToString_CredentialOutcomeAndResponse_OmitsCredentialValues(string scenario)
    {
        var result = CreateResult(scenario);
        var response = StaffCredentialResponse.From(result);

        Assert.Equal(nameof(StaffCredentialResult), result.ToString());
        Assert.Equal(nameof(StaffCredentialResponse), response.ToString());
    }

    private static StaffCredentialResult CreateResult(string scenario) => scenario switch
    {
        "tokens" => new StaffCredentialResult.Tokens("test-access", "test-refresh", Guid.Parse("019a2c63-8c00-7000-8000-000000000001")),
        "link" => new StaffCredentialResult.LinkIssued("test-link"),
        "registration" => new StaffCredentialResult.RegistrationOptions("test-challenge", "{}", null),
        "registration-totp" => new StaffCredentialResult.RegistrationOptions("test-challenge", "{}", "test-totp"),
        "passkey" => new StaffCredentialResult.PasskeyOptions("test-challenge", "{}"),
        "authenticator" => new StaffCredentialResult.AuthenticatorSetup("test-challenge", "test-totp"),
        "factor-totp" => new StaffCredentialResult.FactorRequired("test-challenge", "authenticator", null),
        "factor-passkey" => new StaffCredentialResult.FactorRequired("test-challenge", "authenticatorOrPasskey", "{}"),
        "completed" => new StaffCredentialResult.Completed(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };
}
