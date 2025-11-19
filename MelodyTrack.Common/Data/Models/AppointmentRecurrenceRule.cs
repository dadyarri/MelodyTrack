namespace MelodyTrack.Common.Data.Models;

/// <summary>
///     Rule of recurring for appointment
/// </summary>
public class AppointmentRecurrenceRule : BaseModel
{
    /// <summary>
    ///     The service that this recurrence rule applies to
    /// </summary>
    public required Service Service { get; set; }

    /// <summary>
    ///     The client for whom this recurrence service is scheduled
    /// </summary>
    public required Client Client { get; set; }

    /// <summary>
    ///     The user, who provides this appointment
    /// </summary>
    public User? Provider { get; set; }

    /// <summary>
    ///     Start date of recurrence
    /// </summary>
    public required DateTime StartDate { get; set; }

    /// <summary>
    ///     End date of recurrence. Null if appointment is recurred indefinitely
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    ///     Type of the recurrence (daily, weekly, monthly)
    /// </summary>
    public required RecurrenceType RecurrenceType { get; set; }

    /// <summary>
    ///     Pattern of recurrence (null for daily, bitwise value of selected days for weekly, number of day for monthly)
    /// </summary>
    public required int? RecurrencePattern { get; set; }
}