namespace MelodyTrack.Common.Api.Auth.Requests;

/// <summary>
///     Request to register new user
/// </summary>
public class RegisterRequest
{
    /// <summary>
    ///     Invite code
    /// </summary>
    public required string InviteCode { get; set; }

    /// <summary>
    ///     Email of new user
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    ///     Password of new user
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    ///     First name of new user
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    ///     Last name of new user
    /// </summary>
    public required string LastName { get; set; }
}