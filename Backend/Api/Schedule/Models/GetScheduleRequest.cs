using System.ComponentModel.DataAnnotations;

namespace Backend.Api.Schedule.Models;

public class GetScheduleRequest
{
    [Required] public DateTime StartDate { get; set; }

    [Required] public DateTime EndDate { get; set; }
}