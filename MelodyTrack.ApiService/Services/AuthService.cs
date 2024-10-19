namespace MelodyTrack.ApiService.Services;

using System.Security.Claims;
using System.Security.Cryptography;
using FastEndpoints.Security;
using JetBrains.Annotations;
using MelodyTrack.ApiService.Configuration;
using MelodyTrack.ApiService.Endpoints.Auth.Login;
using MelodyTrack.ApiService.Endpoints.Auth.Logout;
using MelodyTrack.ApiService.Endpoints.Auth.Register;
using MelodyTrack.ApiService.Storage;
using MelodyTrack.ApiService.Storage.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

[PublicAPI]
public class AuthService(AppDbContext db, SecurityConfiguration securityConfiguration) : IService
{
    /// <summary>
    /// Do not change!
    /// </summary>
    private const int SaltSize = 256 / 8;
    private const int KeySize = 256 / 8;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    /// <summary>
    /// Registers a new user with the provided credentials.
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <param name="password">User's plain text password.</param>
    /// <returns>Task that represents the asynchronous operation, containing the result of the registration process.</returns>
    public async Task<bool> RegisterUserAsync(RegisterRequest request, CancellationToken ct)
    {

        if (await db.Users.AnyAsync(u => u.Email == request.Email, ct))
        {
            return false;
        }

        var user = new User
        {
            Email = request.Email,
            DisplayName = request.DisplayName,
            Password = HashPassword(request.Password)
        };

        var result = new LoginResponse
        {
            AccessToken = GenerateAccessToken(user),
            RefreshToken = GenerateRefreshToken(user)
        };

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        return true;

    }

    /// <summary>
    /// Authenticates the user with the given email and password.
    /// Issues a new access token and refresh token if successful.
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <param name="password">User's plain text password.</param>
    /// <returns>Task that represents the asynchronous operation, containing tokens if authentication is successful.</returns>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user == null)
        {
            return null;
        }

        var result = VerifyHashedPassword(user.Password, request.Password);

        if (result != PasswordVerificationResult.Success)
        {
            return null;
        }

        return result == PasswordVerificationResult.Failed ? null : new LoginResponse
        {
            AccessToken = GenerateAccessToken(user),
            RefreshToken = GenerateRefreshToken(user),
        };

    }

    /// <summary>
    /// Generates new access and refresh tokens using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to exchange for a new access token.</param>
    /// <returns>Task that represents the asynchronous operation, containing new tokens.</returns>
    public async Task<LoginResponse?> RefreshTokensAsync(string refreshToken)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(e => e.Token == refreshToken && e.ExpireAt > DateTime.UtcNow);
        if (token == null)
        {
            return null;
        }

        var token = new RefreshToken
        {
            Token = GenerateRefreshToken()
        }
    }

    /// <summary>
    /// Revokes the specified refresh token, invalidating it for future use.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the token was successfully revoked.</returns>
    public Task<bool> RevokeRefreshTokenAsync(string refreshToken) { }

    /// <summary>
    /// Verifies the validity of an access token (e.g., JWT validation).
    /// </summary>
    /// <param name="accessToken">The access token to validate.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the token is valid.</returns>
    public Task<bool> ValidateAccessTokenAsync(string accessToken) { }

    /// <summary>
    /// Initiates the password reset process by sending a reset token to the user's email.
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the reset email was successfully sent.</returns>
    public async Task<bool> InitiatePasswordResetAsync(string email)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return false;
        }

        // TODO: Add sending emails
        return true;
    }

    /// <summary>
    /// Resets the user's password using a valid reset token.
    /// </summary>
    /// <param name="resetToken">The token sent to the user's email for password reset.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the password reset was successful.</returns>
    public async Task<bool> ResetPasswordAsync(string resetToken, string newPassword)
    {

    }

    /// <summary>
    /// Logs the user out by invalidating any active tokens (access and refresh).
    /// </summary>
    /// <param name="userId">The ID of the user to log out.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether logout was successful.</returns>
    public async Task<bool> LogoutAsync(LogoutRequest request) { }

    private string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hashedPasswordByteCount = SaltSize + SaltSize;
        Span<byte> hashedPasswordBytes = stackalloc byte[hashedPasswordByteCount];

        var saltBytes = hashedPasswordBytes.Slice(start: 1, length: SaltSize);
        var keyBytes = hashedPasswordBytes.Slice(start: 1 + SaltSize, length: KeySize);

        RandomNumberGenerator.Fill(saltBytes);
        Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, keyBytes, Iterations, Algorithm);

        return Convert.ToBase64String(hashedPasswordBytes);
    }

    private PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(hashedPassword);
        ArgumentNullException.ThrowIfNull(plainPassword);

        var hashedPasswordByteCount = ComputeDecodedBase64ByteCount(hashedPassword);
        Span<byte> hashedPasswordBytes = stackalloc byte[hashedPasswordByteCount];

        if (!Convert.TryFromBase64String(hashedPassword, hashedPasswordBytes, out _))
        {
            return PasswordVerificationResult.Failed;
        }

        if (hashedPasswordBytes.Length == 0)
        {
            return PasswordVerificationResult.Failed;
        }

        var expectedHashedPasswordLength = SaltSize + KeySize;
        if (hashedPasswordBytes.Length != expectedHashedPasswordLength)
        {
            return PasswordVerificationResult.Failed;
        }

        var saltBytes = hashedPasswordBytes.Slice(start: 1, length: SaltSize);
        var expectedKeyBytes = hashedPasswordBytes.Slice(start: 1 + SaltSize, length: KeySize);

        Span<byte> actualKeyBytes = stackalloc byte[KeySize];
        Rfc2898DeriveBytes.Pbkdf2(plainPassword, saltBytes, actualKeyBytes, Iterations, Algorithm);

        if (!CryptographicOperations.FixedTimeEquals(expectedKeyBytes, actualKeyBytes))
        {
            return PasswordVerificationResult.Failed;
        }

        return PasswordVerificationResult.Success;
    }

    private static int ComputeDecodedBase64ByteCount(string base64Str)
    {
        var characterCount = base64Str.Length;
        var paddingCount = 0;

        if (characterCount > 0)
        {
            if (base64Str[characterCount - 1] == '=')
            {
                paddingCount++;

                if (characterCount > 1 && base64Str[characterCount - 2] == '=')
                {
                    paddingCount++;
                }
            }
        }

        return (characterCount * 3 / 4) - paddingCount;
    }

    private string GenerateAccessToken(User user) => JwtBearer.CreateToken(opts =>
        {
            opts.SigningKey = securityConfiguration.AccessTokenSigningKey;
            opts.ExpireAt = DateTime.UtcNow.AddMinutes(15);
            opts.User.Claims.Add(new Claim(ClaimTypes.Name, user.Email));
            opts.User.Claims.Add(new Claim(JwtRegisteredClaimNames.Jti, GenerateRandomString()));
        });

    private static string GenerateRefreshToken(User user) => Convert.ToBase64String(user.Id.ToByteArray()).TrimEnd('=');

    private static string GenerateRandomString()
    {
        var randomBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return Convert.ToBase64String(randomBytes);
    }
}
