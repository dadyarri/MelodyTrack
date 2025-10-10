using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Api.Auth.Models;
using Backend.Data.Entities;
using FastEndpoints.Security;

namespace Backend.Utils;

public static class UserUtils
{
    public static void CreatePasswordHash(string plainPassword, out byte[] passwordSalt, out byte[] passwordHash)
    {
        using var hmac = new HMACSHA512();
        passwordSalt = hmac.Key;
        passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainPassword));
    }

    public static LoginResponse CreateAccessToken(User user)
    {
        // That's pretty unsecure, but I don't give a shit
        var expireAt = DateTime.UtcNow.AddYears(40);
        return new LoginResponse
        {
            AccessToken = JwtBearer.CreateToken(opts =>
                {
                    opts.SigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("JWT_SIGNING_KEY");
                    opts.ExpireAt = expireAt;
                    opts.User.Claims.Add(new Claim(ClaimTypes.Name, user.Username));
                }
            ),
            ExpireAt = expireAt,
            FirstName = user.FirstName,
            LastName = user.LastName
        };
    }

    public static bool IsValidPassword(User user, LoginRequest req)
    {
        return VerifyPasswordHash(req.Password, user.PasswordHash, user.PasswordSalt);
    }

    private static bool VerifyPasswordHash(string plainPassword, byte[] passwordHash, byte[] passwordSalt)
    {
        using var hmac = new HMACSHA512(passwordSalt);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainPassword));
        return computedHash.SequenceEqual(passwordHash);
    }
}