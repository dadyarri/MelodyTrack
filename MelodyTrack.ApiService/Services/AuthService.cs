namespace MelodyTrack.ApiService.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using Configuration;
using Endpoints.Auth.Login;
using Endpoints.Auth.Logout;
using Endpoints.Auth.Register;
using FastEndpoints.Security;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Storage;
using Storage.Entities;

[PublicAPI]
public class AuthService(AppDbContext db, SecurityConfiguration securityConfiguration)
    : IService
{
    /// <summary>
    /// Do not change!
    /// </summary>
    private const int SaltSize = 256 / 8;

    private const int KeySize = 256 / 8;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    private readonly TokenValidationParameters validationParameters = new()
    {
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey =
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityConfiguration.AccessTokenSigningKey)),
        ClockSkew = TimeSpan.Zero
    };

    /// <summary>
    /// Registers a new user with the provided credentials.
    /// </summary>
    /// <param name="request">Request to register user</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that represents the asynchronous operation, containing the result of the registration process.</returns>
    public async Task<Result<LoginResponse>> RegisterUserAsync(RegisterRequest request, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == request.Email, ct))
        {
            return Result.Conflict();
        }

        var user = new User
        {
            Email = request.Email, DisplayName = request.DisplayName, Password = HashPassword(request.Password)
        };

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success(new LoginResponse
        {
            AccessToken = GenerateAccessToken(user), RefreshToken = GenerateRefreshToken(user)
        });
    }

    /// <summary>
    /// Authenticates the user with the given email and password.
    /// Issues a new access token and refresh token if successful.
    /// </summary>
    /// <param name="request">Request to login user</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that represents the asynchronous operation, containing tokens if authentication is successful.</returns>
    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user == null)
        {
            return Result.Unauthorized();
        }

        var result = VerifyHashedPassword(user.Password, request.Password);

        if (result != PasswordVerificationResult.Success)
        {
            return Result.Unauthorized();
        }

        return Result.Success(BuildLoginResponse(user));
    }

    private LoginResponse BuildLoginResponse(User user) =>
        new() { AccessToken = GenerateAccessToken(user), RefreshToken = GenerateRefreshToken(user), };

    /// <summary>
    /// Generates new access and refresh tokens using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to exchange for a new access token.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that represents the asynchronous operation, containing new tokens.</returns>
    public async Task<Result<LoginResponse>> RefreshTokensAsync(string refreshToken, CancellationToken ct)
    {
        var token = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(e => e.Token == refreshToken && e.ExpireAt > DateTime.UtcNow && !e.Revoked, ct);

        if (token == null)
        {
            return Result.Unauthorized();
        }

        var rt = GenerateRefreshToken(token.User);
        var at = GenerateAccessToken(token.User);

        token = new RefreshToken { Token = rt, User = token.User };

        await db.RefreshTokens.AddAsync(token, ct);
        await db.SaveChangesAsync(ct);

        return Result.Success(new LoginResponse { AccessToken = at, RefreshToken = rt });
    }

    /// <summary>
    /// Revokes the specified refresh token, invalidating it for future use.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the token was successfully revoked.</returns>
    public async Task<Result> RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var token = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(e => e.Token == refreshToken && e.ExpireAt > DateTime.UtcNow, ct);

        if (token == null)
        {
            return Result.Unauthorized();
        }

        token.Revoked = true;

        await db.SaveChangesAsync(ct);

        return Result.NoContent();
    }

    /// <summary>
    /// Verifies the validity of an access token (e.g., JWT validation).
    /// </summary>
    /// <param name="accessToken">The access token to validate.</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether the token is valid.</returns>
    public async Task<bool> ValidateAccessTokenAsync(string accessToken)
    {
        var tokenValidationParameters = validationParameters;

        var tokenHandler = new JwtSecurityTokenHandler();

        var result = await tokenHandler.ValidateTokenAsync(accessToken, tokenValidationParameters);

        if (result is null) { return false; }

        var foundJti = result.Claims.TryGetValue(JwtRegisteredClaimNames.Jti, out var jti);

        if (!foundJti) { return false; }

        return !await db.BannedTokens.AnyAsync(e => e.Jti == (string)jti!);
    }

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
    /// <param name="request">Request to logout user</param>
    /// <returns>Task that represents the asynchronous operation, indicating whether logout was successful.</returns>
    public async Task<bool> LogoutAsync(LogoutRequest request)
    {

    }

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
