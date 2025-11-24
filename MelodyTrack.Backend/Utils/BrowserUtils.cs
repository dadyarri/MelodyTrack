using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using Serilog;

namespace MelodyTrack.Backend.Utils;

public static partial class BrowserUtils
{
    public static string GetDeviceInfo(StringValues uaHeader)
    {
        var ua = uaHeader.ToString();

        if (string.IsNullOrWhiteSpace(ua))
        {
            return "Unknown Device";
        }

        // 1. Handle Custom App: MelodyTrackWeb
        // Pattern: MelodyTrackWeb/2.0
        var appMatch = MelodyTrackUaRegex().Match(ua);
        if (appMatch.Success)
        {
            var version = appMatch.Groups[1].Value;
            return $"MelodyTrackWeb {version}";
        }

        // 2. Detect Operating System
        var os = "Unknown OS";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            os = "Windows";
        }
        else if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            os = "Android";
        }
        else if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            os = "Linux";
        }
        else if (ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("Macintosh"))
        {
            os = "iOS/macOS";
        }

        // 3. Detect Browser and Version
        var browser = "Unknown Browser";
        var browserVersion = "";

        // Order matters: Edge often contains "Chrome", Chrome often contains "Safari"
        if (ua.Contains("Edg"))
        {
            browser = "Edge";
            browserVersion = GetVersion(ua, "Edg");
        }
        else if (ua.Contains("Chrome"))
        {
            browser = "Chrome";
            browserVersion = GetVersion(ua, "Chrome");
        }
        else if (ua.Contains("Firefox"))
        {
            browser = "Firefox";
            browserVersion = GetVersion(ua, "Firefox");
        }
        else if (ua.Contains("Safari"))
        {
            browser = "Safari";
            browserVersion = GetVersion(ua, "Version");
        }

        return $"{browser} {browserVersion} ({os})".Trim();
    }

    private static string GetVersion(string ua, string token)
    {
        try
        {
            var match = Regex.Match(ua, $@"{token}[\/ ]([\d\.]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch
        {
            Log.Logger.Warning("Parsing UA error");
        }
        return "";
    }

    [GeneratedRegex(@"MelodyTrackWeb\/([\d\.]+)")]
    private static partial Regex MelodyTrackUaRegex();
}