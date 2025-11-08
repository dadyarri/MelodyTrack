namespace MelodyTrack.Backend.Data.Models;

/// <summary>
///     Database represenation of user, who have access to data
/// </summary>
public class User : BaseModel
{
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
    public required string Email { get; set; }

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

    /// <summary>
    ///     Determines, if user failed too much login attempts and is locked out for some time
    /// </summary>
    public DateTime? LockedUntil { get; set; }
}