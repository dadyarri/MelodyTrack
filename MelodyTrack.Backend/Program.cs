using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
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
    builder.Services.SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Melody Track API";
            s.Version = "v1";
        };
    });
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN"))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // Database configuration
    if (builder.Environment.IsProduction())
    {
        var connectionString = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
        builder.Services.AddDbContextPool<AppDbContext>(opts => opts.UseNpgsql(connectionString)
        );
        Log.Information("Using PostgreSQL database in production");
    }
    else
    {
        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "database.db");
        builder.Services.AddDbContextPool<AppDbContext>(opts => opts.UseSqlite($"Data Source={dbPath}")
        );
        Log.Information("Using SQLite database in development at {DatabasePath}", dbPath);
    }

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCors("AllowFrontend");
    app.UseFastEndpoints();
    app.UseSerilogRequestLogging();
    app.UseSwaggerGen();

    app.Run();
    return 0;
}
catch (HostAbortedException)
{
    Log.Warning("Host was aborted");
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