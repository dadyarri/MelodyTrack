using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Exceptions;
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
    var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");

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
            policy.WithOrigins(appDomain)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // Database configuration

    var connectionString = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
    builder.Services.AddDbContextPool<AppDbContext>(opts => opts.UseNpgsql(connectionString)
    );
    Log.Information("Using PostgreSQL database");

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCors("AllowFrontend");
    app.UseFastEndpoints();
    app.UseSerilogRequestLogging();
    app.UseSwaggerGen();

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.SUPERUSER);

    if (superuserRole == null)
    {
        throw new MissingRoleInDatabaseException(UserRoles.SUPERUSER);
    }

    var hasSuperuser = await db.Users
        .AsNoTracking()
        .Include(e => e.Role)
        .AnyAsync(e => e.Role == superuserRole);

    if (!hasSuperuser)
    {
        var inviteCode = Ulid.NewUlid();
        var url = $"{appDomain}/invite?code={inviteCode}";
        await db.InviteCodes.AddAsync(new InviteCode
        {
            Id = new Ulid(),
            Code = inviteCode,
            Role = superuserRole,
            ValidUntil = DateTime.UtcNow.AddDays(2)
        });
        await db.SaveChangesAsync();
        Log.Warning("Superuser was not created yet. Use this link to create a new superuser: {Link}", url);
    }

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