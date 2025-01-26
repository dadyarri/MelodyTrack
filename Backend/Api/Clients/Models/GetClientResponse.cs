using Backend.Data.Entities;

namespace Backend.Api.Clients.Models;

public class GetClientResponse
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Patronymic { get; set; }
    public required ClientContact? Contacts { get; set; }
    public required List<Payment> LatestPayments { get; set; } = [];
}