using Backend.Data.Entities;

namespace Backend.Api.Clients.Models;

public class ClientWithBalanceResponse
{
    public required long Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Patronymic { get; set; }
    public required ClientContact? Contacts { get; set; }
    public required decimal Balance { get; set; }
} 