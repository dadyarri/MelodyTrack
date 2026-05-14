namespace MelodyTrack.Backend.Exceptions;

public class RequiredStartupFileNotFoundException(string path)
    : Exception($"Required startup file was not found: {path}");
