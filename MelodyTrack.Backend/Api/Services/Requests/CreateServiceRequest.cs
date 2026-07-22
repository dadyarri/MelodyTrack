namespace MelodyTrack.Backend.Api.Services.Requests;

public class CreateServiceRequest
{
    public required string Name { get; set; }
    public string? PublicName { get; set; }
    public string? Description { get; set; }
    public required decimal Price { get; set; }
    public bool IsConsultation { get; set; }
}
