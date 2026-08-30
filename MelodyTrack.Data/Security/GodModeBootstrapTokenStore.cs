using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelodyTrack.Core.Configuration;
using Microsoft.AspNetCore.WebUtilities;

namespace MelodyTrack.Data.Security;

public static class GodModeBootstrapTokenStore
{
    private const string TokenFileExtension = ".json";

    public static async Task<string> IssueAsync(
        GodModeOptions options,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        Directory.CreateDirectory(options.StateDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                options.StateDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var tokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var record = new GodModeBootstrapTokenRecord(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(options.BootstrapTokenLifetimeMinutes));
        var path = Path.Combine(options.StateDirectory, $"{tokenId}{TokenFileExtension}");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record), ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return $"{tokenId}.{secret}";
    }

    public static async Task<bool> ConsumeAsync(
        GodModeOptions options,
        string token,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var tokenParts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokenParts.Length != 2 || !IsSafeTokenId(tokenParts[0]))
        {
            return false;
        }

        var tokenPath = Path.Combine(options.StateDirectory, $"{tokenParts[0]}{TokenFileExtension}");
        var consumingPath = Path.Combine(options.StateDirectory, $"{tokenParts[0]}.{Guid.NewGuid():N}.consuming");
        try
        {
            File.Move(tokenPath, consumingPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            var record = JsonSerializer.Deserialize<GodModeBootstrapTokenRecord>(
                await File.ReadAllTextAsync(consumingPath, ct));
            if (record is null || record.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
            {
                return false;
            }

            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(tokenParts[1]));
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(record.TokenHash),
                suppliedHash);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            File.Delete(consumingPath);
        }
    }

    private static bool IsSafeTokenId(string value) =>
        value.Length == 24 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record GodModeBootstrapTokenRecord(string TokenHash, DateTime ExpiresAtUtc);
}
