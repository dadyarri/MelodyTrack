namespace MelodyTrack.Backend.Exceptions;

public class RequiredEnviromentVariableNotFoundException(string name)
    : Exception($"Required environment variable {name} was not set");