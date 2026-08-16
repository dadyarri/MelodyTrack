using MelodyTrack.Data;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!TryParseMode(args, out var mode))
{
    Console.Error.WriteLine("Usage: dotnet MelodyTrack.Init.dll --mode <production|development|test>");
    return 2;
}

var environmentName = mode switch
{
    InitializationMode.Development => Environments.Development,
    InitializationMode.Test => "Test",
    _ => Environments.Production
};

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ApplicationName = "MelodyTrack.Init",
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = environmentName
});
builder.Configuration.AddInMemoryCollection(LegacyConfiguration.ReadEnvironmentAliases());
builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthenticationSecretsOptions(builder.Configuration);
builder.Services.AddPublicUrlOptions(builder.Configuration);
builder.Services.AddMelodyTrackData(builder.Configuration);
builder.Services.AddMelodyTrackInitialization(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MelodyTrack.Init");
using var cancellationSource = new CancellationTokenSource();
var hostStarted = false;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    await host.StartAsync(cancellationSource.Token);
    hostStarted = true;

    await using var scope = host.Services.CreateAsyncScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
    await initializer.RunAsync(mode, cancellationSource.Token);
    return 0;
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
    logger.LogWarning("Database initialization was cancelled");
    return 130;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Database initialization failed; Backend must not be started");
    return 1;
}
finally
{
    if (hostStarted)
    {
        await host.StopAsync(CancellationToken.None);
    }
}

static bool TryParseMode(string[] arguments, out InitializationMode mode)
{
    mode = default;
    var modeIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase));
    if (modeIndex < 0 || modeIndex == arguments.Length - 1)
    {
        return false;
    }

    return Enum.TryParse(arguments[modeIndex + 1], ignoreCase: true, out mode);
}
