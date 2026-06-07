using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;

namespace MelodyTrack.Backend.Extensions;

public static class UserQueryExtensions
{
    public static IQueryable<User> WhereEmailMatches(this IQueryable<User> users, string email)
    {
        var blindIndex = UserUtils.HashEmailBlindIndex(email);
        return users.Where(user => user.EmailBlindIndex == blindIndex);
    }
}
