using MelodyTrack.Data;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Initialization;
using MelodyTrack.Init;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

if (GodModeCommand.IsRequested(args))
{
    try
    {
        return await GodModeCommand.RunAsync(cancellationSource.Token);
    }
    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
    {
        Console.Error.WriteLine("God mode link generation was cancelled.");
        return 130;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"God mode link generation failed: {exception.Message}");
        return 1;
    }
}

if (!TryParseMode(args, out var mode) || !TryParseRecovery(args, out var recoveryEmail, out var showRecoveryUrl))
{
    Console.Error.WriteLine("Usage: dotnet MelodyTrack.Init.dll --mode <production|development|test> [--recover-superuser <email> --show-recovery-url] | god-mode");
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
builder.AddServiceDefaults("melodytrack-init");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthenticationSecretsOptions(builder.Configuration);
builder.Services.AddPublicUrlOptions(builder.Configuration);
builder.Services.AddMelodyTrackData(builder.Configuration);
builder.Services.AddMelodyTrackInitialization(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MelodyTrack.Init");
var hostStarted = false;

try
{
    await host.StartAsync(cancellationSource.Token);
    hostStarted = true;

    await using var scope = host.Services.CreateAsyncScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
    await initializer.RunAsync(mode, cancellationSource.Token);
    if (recoveryEmail is not null)
    {
        var recovery = scope.ServiceProvider.GetRequiredService<SuperuserRecoveryService>();
        var result = await recovery.CreateResetUrlAsync(recoveryEmail, cancellationSource.Token);
        Console.Out.WriteLine($"Reset URL: {result.ResetUrl}");
        Console.Out.WriteLine($"One-time recovery code: {result.RecoveryCode}");
    }
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

static bool TryParseRecovery(string[] arguments, out string? email, out bool showRecoveryUrl)
{
    email = null;
    showRecoveryUrl = arguments.Contains("--show-recovery-url", StringComparer.OrdinalIgnoreCase);
    var recoveryIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--recover-superuser", StringComparison.OrdinalIgnoreCase));
    if (recoveryIndex < 0)
    {
        return !showRecoveryUrl;
    }

    if (recoveryIndex == arguments.Length - 1 || !showRecoveryUrl)
    {
        return false;
    }

    email = arguments[recoveryIndex + 1];
    return !string.IsNullOrWhiteSpace(email);
}
