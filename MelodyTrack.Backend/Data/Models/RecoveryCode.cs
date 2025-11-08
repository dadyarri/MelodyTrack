namespace MelodyTrack.Backend.Data.Models;

public class RecoveryCode : BaseModel
{
    /// <summary>
    ///     One-time recovery code
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    ///     User, for who the recovery code was generated
    /// </summary>
    public required User User { get; set; }

    /// <summary>
    ///     Mark, if the recovery code was used
    /// </summary>
    public bool WasUsed { get; set; }
}