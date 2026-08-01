using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.ClientPortal;

public static class SavedClientPortalIdentityMapper
{
    public static SavedClientPortalIdentityResponse ToResponse(User user, string reference, DateTime lastUsedAtUtc)
    {
        return new SavedClientPortalIdentityResponse
        {
            IdentityId = user.ClientId?.ToString() ?? user.Id.ToString(),
            Reference = reference,
            DisplayLabel = BuildDisplayLabel(user.FirstName, user.LastName),
            LastUsedAtUtc = lastUsedAtUtc
        };
    }

    public static string BuildDisplayLabel(string firstName, string lastName)
    {
        var lastNameInitial = string.IsNullOrWhiteSpace(lastName) ? null : $" {char.ToUpperInvariant(lastName[0])}.";
        return $"{firstName}{lastNameInitial}";
    }
}
