namespace Backend.Data.Entities;

public class ServiceHistory : BaseModel
{
    public required Service Service { get; set; } = null!;

    public required Client Client { get; set; } = null!;

    public required DateTime StartDate { get; set; }

    public required DateTime EndDate { get; set; }

    public bool Completed { get; set; } = false;
}