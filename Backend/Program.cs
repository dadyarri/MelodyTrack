using Backend.Data;
using Backend.Utils;
using FastEndpoints;
using FastEndpoints.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(theme: TemplateTheme.Code))
    .CreateLogger();

using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests()
    .TraceToSharedLogger();

Log.Information("Starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddAuthenticationJwtBearer(opts =>
    {
        opts.SigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("JWT_SIGNING_KEY");
    });

    builder.Services.AddAuthorization();
    builder.Services.AddFastEndpoints();
    builder.Services.AddSerilog();
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "database.db");

    Log.Debug("Database is stored in {DatabasePath}", dbPath);

    builder.Services.AddDbContextPool<AppDbContext>(
        opts => opts.UseSqlite($"Data Source={dbPath}")
    );

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseFastEndpoints();
    app.UseSerilogRequestLogging();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}