namespace MelodyTrack.Backend.Exceptions;

public class InvalidEnvironmentVariableException(string name, string reason)
    : Exception($"Environment variable {name} is invalid: {reason}");
