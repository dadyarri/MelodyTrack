namespace MelodyTrack.Common.Api.Clients.Responses;

public class GetClientsWithNegativeBalanceResponse
{
    public List<ClientWithBalanceDto> Debtors { get; set; } = [];
}