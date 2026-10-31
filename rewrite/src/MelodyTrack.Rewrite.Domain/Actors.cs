namespace MelodyTrack.Rewrite.Domain;

public closed record Actor
{
    public sealed record Staff(StaffId Id, StaffRole Role) : Actor;
    public sealed record Client(ClientId Id) : Actor;
}
