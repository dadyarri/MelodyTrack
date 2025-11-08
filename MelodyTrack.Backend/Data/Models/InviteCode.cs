namespace MelodyTrack.Backend.Data.Models;

public class InviteCode : BaseModel
{
    /// <summary>
    ///     Single-time invite code
    /// </summary>
    public required Ulid Code { get; set; }

    /// <summary>
    ///     Timestamp, before that the code is valid
    /// </summary>
    public required DateTime ValidUntil { get; set; }

    /// <summary>
    ///     Mark, if the invite code was used before
    /// </summary>
    public bool WasUsed { get; set; }

    /// <summary>
    ///     Role, which user should be created with
    /// </summary>
    public required Role Role { get; set; }

    /// <summary>
    ///     Email of potential user, for whom the invite code was created
    /// </summary>
    public string? Email { get; set; }
}