namespace MelodyTrack.Backend.Data.Enums;

public enum VacationRequestAction
{
    Create,
    Update,
    Delete
}

public enum VacationRequestStatus
{
    Pending,
    Approved,
    Declined,
    Cancelled
}

public enum VacationRequestPrincipalType
{
    Staff,
    Client
}

public enum VacationRequestSubjectType
{
    Staff,
    Client
}
