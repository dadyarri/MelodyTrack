using MelodyTrack.Common.Data.Enums;

namespace MelodyTrack.Backend.Exceptions;

public class MissingRoleInDatabaseException(UserRoles role) : Exception($"Database does not contain role {role}");