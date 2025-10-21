using Microsoft.Extensions.Primitives;

namespace MelodyTrack.Backend.Utils;

public static class BrowserUtils
{
    public static string GetDeviceInfo(StringValues ua)
    {
        var browser = new Kong.Browser(ua);

        var os = browser switch
        {
            { Windows: true } => "Windows",
            { Android: true } => "Android",
            { Linux: true } => "Linux",
            { iOS: true } => "iOS",
            { Mac: true } => "Mac",
            _ => "Unknown"
        };

        return $"{string.Join(' ', browser.Name, browser.Version)} на {string.Join(' ', os, browser.OSVersion)}";
    }
}