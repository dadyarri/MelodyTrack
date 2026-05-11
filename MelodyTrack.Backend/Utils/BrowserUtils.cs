using UaDetector;

namespace MelodyTrack.Backend.Utils;

public static class BrowserUtils
{
    private const string UnknownDeviceInfo = "Неизвестное устройство";

    public static string GetDeviceInfo(IHeaderDictionary headers, IUaDetector uaDetector)
    {
        var userAgent = headers.UserAgent.ToString();
        var parsedHeaders = headers.ToDictionary(e => e.Key, e => e.Value.ToArray().FirstOrDefault());

        if (!uaDetector.TryParse(userAgent, parsedHeaders, out var result))
        {
            return UnknownDeviceInfo;
        }

        var browser = string.Join(' ', result.Browser?.Name ?? "Unknown", result.Browser?.Version)
            .Trim();
        var os = string.Join(' ', result.Os?.Name ?? "Unknown", result.Os?.Version)
            .Trim();

        if (string.IsNullOrWhiteSpace(browser) || string.IsNullOrWhiteSpace(os))
        {
            return UnknownDeviceInfo;
        }

        return $"{browser} на {os}";
    }
}
