using MelodyTrack.Backend.Utils;

namespace MelodyTrack.Backend.Data.Models;

/// <summary>
///     Database representation of user, who have access to data
/// </summary>
public class User : BaseModel
{
    private string _email = null!;

    /// <summary>
    ///     First name of the user
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    ///     Last name of the user
    /// </summary>
    public required string LastName { get; set; }

    /// <summary>
    ///     Email of the user
    /// </summary>
    public required string Email
    {
        get => _email;
        set
        {
            var normalizedEmail = UserUtils.NormalizeEmail(value);
            _email = normalizedEmail;
            EmailBlindIndex = UserUtils.HashEmailBlindIndex(normalizedEmail);
        }
    }

    public string EmailBlindIndex { get; private set; } = null!;

    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    ///     Hashed password of the user
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    ///     Secret for TOTP of the user
    /// </summary>
    public string? TotpSecret { get; set; }

    /// <summary>
    ///     Role of the current user
    /// </summary>
    public required Role Role { get; set; }

    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>
    ///     Determines, if user failed too much login attempts and is locked out for some time
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    public UserOnboardingState? OnboardingState { get; set; }
    public List<UserWorkingHoursDay> WorkingHours { get; set; } = [];
    public List<UserVacation> Vacations { get; set; } = [];
}
