using System.Security.Cryptography;
using MelodyTrack.Data.Security;
using Microsoft.IdentityModel.Tokens;

namespace MelodyTrack.Backend.Api.Auth;

public static class JwtKeyMaterial
{
    public static ECDsaSecurityKey CreateValidationKey(string encodedPrivateKey)
    {
        var privateKey = AuthenticationSecretMaterial.DecodeP256PrivateKey(encodedPrivateKey);
        using var signingAlgorithm = ECDsa.Create();
        signingAlgorithm.ImportPkcs8PrivateKey(privateKey, out _);
        var publicKey = signingAlgorithm.ExportSubjectPublicKeyInfo();
        var validationAlgorithm = ECDsa.Create();
        validationAlgorithm.ImportSubjectPublicKeyInfo(publicKey, out _);
        return new ECDsaSecurityKey(validationAlgorithm);
    }
}
