using System.ComponentModel.DataAnnotations;

namespace Backend.Api.Payments.Models;

public class CreatePaymentRequest
{
    public required long ClientId { get; set; }

    [Range(0, (double)decimal.MaxValue)] public required decimal Amount { get; set; }

    public required DateTime Date { get; set; } = DateTime.UtcNow;

    public required string Description { get; set; } = string.Empty;
}