using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Initialization;
using MelodyTrack.Data.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MelodyTrack.Init;

internal static class GodModeCommand
{
    public static bool IsRequested(string[] args)
    {
        if (args.Length == 1)
        {
            return IsCommand(args[0]);
        }

        return args.Length == 3
               && (IsCommand(args[0]) && IsModePair(args[1], args[2])
                   || IsModePair(args[0], args[1]) && IsCommand(args[2]));
    }

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Production;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = configuration.GetSection(GodModeOptions.SectionName).Get<GodModeOptions>()
            ?? new GodModeOptions();
        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri)
            || publicBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("GodMode:PublicBaseUrl must be an absolute HTTPS URL.");
        }

        _ = AuthenticationSecretMaterial.DecodeSymmetricKey(
            options.SessionSigningKey,
            "GodMode:SessionSigningKey");
        var token = await GodModeBootstrapTokenStore.IssueAsync(options, TimeProvider.System, ct);
        Console.WriteLine("Одноразовая ссылка аварийного доступа (действует недолго и используется один раз):");
        Console.WriteLine($"{options.PublicBaseUrl.TrimEnd('/')}/god-mode/#token={token}");
        return 0;
    }

    private static bool IsCommand(string value) =>
        string.Equals(value, "god-mode", StringComparison.OrdinalIgnoreCase);

    private static bool IsModePair(string option, string value) =>
        string.Equals(option, "--mode", StringComparison.OrdinalIgnoreCase)
        && Enum.TryParse<InitializationMode>(value, ignoreCase: true, out _);
}
