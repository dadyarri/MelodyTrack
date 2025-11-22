using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend;
using MelodyTrack.Backend.Exceptions;
using MelodyTrack.Backend.Jobs;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NSwag;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

var logLevelSwitch = new LoggingLevelSwitch();

var environment = EnvironmentUtils.GetRequiredEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
logLevelSwitch.MinimumLevel = environment == "Development"
    ? LogEventLevel.Debug
    : LogEventLevel.Information;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .MinimumLevel.ControlledBy(logLevelSwitch)
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
    .CreateLogger();

using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests()
    .TraceToSharedLogger();

// var scheduler = 

Log.Information("Starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);
    var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");

    builder.Services.AddAuthenticationJwtBearer(opts =>
    {
        opts.SigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY");
    });

    builder.Services.AddAuthorization();
    builder.Services.AddFastEndpoints(x => { x.SourceGeneratorDiscoveredTypes = DiscoveredTypes.All; });
    builder.Services.AddSerilog();
    builder.Services.SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Melody Track API";
            s.Version = "v2";
            s.DocumentName = "v2";
            s.PostProcess = document =>
            {
                foreach (var op in document.Operations)
                {
                    if (op.Operation.Security is not null && op.Operation.Security.Count > 0)
                    {
                        op.Operation.Parameters.Add(new OpenApiHeader
                        {
                            Name = "Authorization",
                            Description = "Bearer token",
                            IsRequired = true,
                            Kind = OpenApiParameterKind.Header
                        });
                    }
                }
            };
        };
        o.ShortSchemaNames = true;
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

    // Custom services
    builder.Services.AddScoped<ClientToClientWithBalanceDtoMapConfig>();
    builder.Services.AddScoped<ServiceToServiceWithCurrentPriceDtoMapConfig>();

    builder.Services.Configure<QuartzOptions>(opts =>
    {
        opts.Scheduling.IgnoreDuplicates = true;
        opts.Scheduling.OverWriteExistingData = true;
    });
    builder.Services.AddQuartz(q =>
    {
        q.UseDefaultThreadPool(x => x.MaxConcurrency = 3);
        q.UsePersistentStore(x =>
        {
            x.UseProperties = true;
            x.UsePostgres(connectionString);
            x.UseSystemTextJsonSerializer();
        });
        q.AddJob<CreateRecurringAppointments>(opts =>
        {
            opts.WithIdentity(CreateRecurringAppointments.Key);
        });
        q.AddTrigger(opts =>
        {
            opts.ForJob(CreateRecurringAppointments.Key);
            opts.WithIdentity("CreateRecurringAppointments-trigger");
            opts.WithCronSchedule("0 0 12 ? * 1");
        });
    });
    builder.Services.AddQuartzServer(q =>
    {
        q.WaitForJobsToComplete = true;
    });

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCors("AllowFrontend");
    app.UseExceptionHandler(errApp =>
    {
        errApp.Run(async ctx =>
        {
            var exHandlerFeature = ctx.Features.Get<IExceptionHandlerFeature>();

            if (exHandlerFeature is not null)
            {
                var route = exHandlerFeature.Endpoint?.DisplayName?.Split(" => ")[0].Replace("HTTP: ", string.Empty);
                var exceptionType = exHandlerFeature.Error.GetType().Name;
                var reason = exHandlerFeature.Error.Message;

                Log.Logger.Error(exHandlerFeature.Error, "Произошла ошибка {Exception} во время выполнения {Route}: {Reason}{StackTrace}", exceptionType, route, reason,
                    exHandlerFeature.Error.StackTrace);

                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(
                    ApiResponse.Failure([
                        new ApiError
                        {
                            Message = $"Произошла ошибка {exceptionType} во время выполнения {route}: {reason}",
                            Code = "UNEXPECTED_SHIT"
                        }
                    ], "Непредвиденная ошибка!"),
                    ctx.RequestAborted);
            }
        });
    });
    app.UseFastEndpoints(x =>
    {
        x.Errors.ResponseBuilder = (failures, httpContext, statusCode) => ApiResponse.Failure(failures.ToApiErrors(), "Ошибка валидации");
        x.Errors.ProducesMetadataType = typeof(ApiResponse<>);
        x.Endpoints.ShortNames = true;
    });
    app.UseSerilogRequestLogging();
    app.UseSwaggerGen();

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (environment == "Test")
    {
        await db.Database.MigrateAsync();
    }

    var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser);

    if (superuserRole == null)
    {
        throw new MissingRoleInDatabaseException(UserRoles.Superuser);
    }

    var hasSuperuser = await db.Users
        .AsNoTracking()
        .Include(e => e.Role)
        .AnyAsync(e => e.Role == superuserRole);

    var inviteCode = await db.InviteCodes
        .AsNoTracking()
        .Include(e => e.Role)
        .FirstOrDefaultAsync(e => e.Role == superuserRole && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow);

    if (!hasSuperuser)
    {
        Ulid code;
        if (inviteCode is null)
        {
            code = Ulid.NewUlid();
            await db.InviteCodes.AddAsync(new InviteCode
            {
                Id = Ulid.NewUlid(),
                Code = code,
                Role = superuserRole,
                ValidUntil = DateTime.UtcNow.AddDays(2)
            });
            await db.SaveChangesAsync();
        }
        else
        {
            code = inviteCode.Code;
        }

        var url = UserUtils.GetInviteUrl(code);
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

namespace MelodyTrack.Backend
{
    public class Program;
}