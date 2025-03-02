using System.ComponentModel.DataAnnotations;

namespace Backend.Api.Services.Models;

public class CreateServiceRequest
{
    [MaxLength(200)] public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }
}