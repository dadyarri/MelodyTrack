using MelodyTrack.Common.Exceptions;

namespace MelodyTrack.Common.Utils;

public static class EnvironmentUtils
{
    public static string GetRequiredEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? throw new RequiredEnvironmentVariableNotFoundException(name);
    }
}