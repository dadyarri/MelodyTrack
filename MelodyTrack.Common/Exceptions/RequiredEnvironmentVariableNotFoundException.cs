namespace MelodyTrack.Common.Exceptions;

public class RequiredEnvironmentVariableNotFoundException(string name)
    : Exception($"Required environment variable {name} was not set");