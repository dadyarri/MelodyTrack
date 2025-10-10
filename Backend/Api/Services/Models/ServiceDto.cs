namespace Backend.Api.Services.Models;

public class ServiceDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
}