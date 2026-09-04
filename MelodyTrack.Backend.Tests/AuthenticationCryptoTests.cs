using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class AuthenticationCryptoTests
{
    private const string JwtPrivateKey = "base64:MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg1a+XfTTbRx+lAZXtBVgkgxPy4juOyvu9VuwfrFCy9BihRANCAATHVVdEpzPvwGWCKZ7kcmGIqi6JGlxlaa6/mELjK19tAuNSLWWbhxeWb0LaVYdquLVhzFnyWL1XsTRPxSen4PvA";

    [Fact]
    public void HashCredentials_PasswordAndPortalPin_UsesVersionedPurposeSeparatedArgon2id()
    {
        var hasher = new CredentialHasher(Options.Create(CreateSecrets()));

        var passwordHash = hasher.HashPassword("correct horse battery staple");
        var pinHash = hasher.HashPortalPin("1234");

        passwordHash.ShouldStartWith("mt-argon2id-v1$");
        pinHash.ShouldStartWith("mt-argon2id-v1$");
        hasher.VerifyPassword(passwordHash, "correct horse battery staple").ShouldBeTrue();
        hasher.VerifyPortalPin(pinHash, "1234").ShouldBeTrue();
        hasher.VerifyPassword(pinHash, "1234").ShouldBeFalse();
        hasher.VerifyPortalPin(passwordHash, "correct horse battery staple").ShouldBeFalse();
    }

    [Fact]
    public void CreateAccessToken_RepeatedCalls_UsesEs256AndIdentitySessionClaimsOnly()
    {
        var service = new JwtTokenService(
            Options.Create(CreateSecrets()),
            Options.Create(new JwtOptions
            {
                Issuer = "MelodyTrack",
                Audience = "MelodyTrack.Web",
                AccessTokenLifetimeMinutes = 10
            }));
        var user = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = "Test",
            LastName = "Admin",
            Email = "admin@example.test",
            Password = "hash",
            Role = new Role { Id = Ulid.NewUlid(), RoleName = UserRoles.Admin, DisplayName = "Administrator" }
        };
        var sessionId = Ulid.NewUlid();

        var encoded = service.CreateAccessToken(user, sessionId);
        var secondEncoded = service.CreateAccessToken(user, sessionId);
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.ReadJwtToken(encoded);
        var secondToken = tokenHandler.ReadJwtToken(secondEncoded);

        foreach (var createdToken in new[] { token, secondToken })
        {
            createdToken.Header.Alg.ShouldBe(SecurityAlgorithms.EcdsaSha256);
            createdToken.Issuer.ShouldBe("MelodyTrack");
            createdToken.Audiences.ShouldContain("MelodyTrack.Web");
            createdToken.Claims.ShouldContain(claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == user.Id.ToString());
            createdToken.Claims.ShouldContain(claim => claim.Type == ClaimTypes.Sid && claim.Value == sessionId.ToString());
            createdToken.Claims.ShouldNotContain(claim => claim.Type == ClaimTypes.Role || claim.Type == "role");
        }
    }

    private static AuthenticationSecretsOptions CreateSecrets() => new()
    {
        JwtSigningPrivateKey = JwtPrivateKey,
        PasswordPepper = "base64:G2UfJdjsXXVuK72YyyE+thhGeWP+luj3S6ifPMqjZtA=",
        PortalPinPepper = "base64:VFWWTyDfkCqiB2TC7OrIQpT8FyXZRCuALw2YJbQDcPw=",
        RefreshTokenHashKey = "base64:5sXZ/oCgEMjrXA1KzQGzAkN88oDl4GZS6gefagjMjW4=",
        CsrfSigningKey = "base64:NWgzsvzLSMFqAg08Nh5+7TE7dbd/paept2GeaGandu0="
    };
}
