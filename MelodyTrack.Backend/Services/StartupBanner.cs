namespace MelodyTrack.Backend.Services;

internal static class StartupBanner
{
    public static string Render(string version, string codename) =>
        $$"""
           __  __      _           _       _____               _
          |  \/  | ___| | ___   __| |_   _|_   _| __ __ _  ___| | __
          | |\/| |/ _ \ |/ _ \ / _` | | | | | || '__/ _` |/ __| |/ /
          | |  | |  __/ | (_) | (_| | |_| | | || | | (_| | (__|   <
          |_|  |_|\___|_|\___/ \__,_|\__, | |_||_|  \__,_|\___|_|\_\
                                      |___/

        MelodyTrack · Version {{version}} · {{codename}}
        """;
}
