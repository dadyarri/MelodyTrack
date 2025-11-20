namespace MelodyTrack.Common.Utils;

public static class ReflectionUtils
{
    public static string ToQueryString<T>(this T obj) where T : class
    {
        return string.Join("&",
            obj.GetType()
                .GetProperties()
                .Where(p => p.GetValue(obj) != null)
                .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.GetValue(obj)!.ToString() ?? string.Empty)}")
        );
    }
}