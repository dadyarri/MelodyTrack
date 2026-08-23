using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MelodyTrack.Backend.Api.Auth;

public sealed class JwtTokenService(
    IOptions<AuthenticationSecretsOptions> authenticationSecrets,
    IOptions<JwtOptions> jwtOptions)
{
    private readonly byte[] _privateKey = AuthenticationSecretMaterial.DecodeP256PrivateKey(
        authenticationSecrets.Value.JwtSigningPrivateKey);
    private readonly JwtOptions _options = jwtOptions.Value;

    public string CreateAccessToken(User user, Ulid? sessionId = null, TimeProvider? timeProvider = null)
    {
        var nowUtc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email)
        };
        if (sessionId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.Sid, sessionId.Value.ToString()));
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(_privateKey, out _);
        var securityKey = new ECDsaSecurityKey(ecdsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory
            {
                CacheSignatureProviders = false
            }
        };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: nowUtc.AddMinutes(_options.AccessTokenLifetimeMinutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
