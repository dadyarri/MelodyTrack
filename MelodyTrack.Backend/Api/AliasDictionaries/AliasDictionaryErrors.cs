using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.AliasDictionaries;

internal static class AliasDictionaryErrors
{
    public static bool IsUniqueViolation(Exception exception)
    {
        return exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            || exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
    }
}
