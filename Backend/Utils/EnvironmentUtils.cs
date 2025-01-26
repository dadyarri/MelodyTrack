namespace Backend.Utils;

public static class EnvironmentUtils
{
    public static string GetRequiredEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ??
               throw new ApplicationException($"{name} is missing");
    }
}