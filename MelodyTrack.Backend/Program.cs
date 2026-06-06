using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Api.Auth.PreProcessors;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Exceptions;
using MelodyTrack.Backend.Jobs;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Utils;
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
using UaDetector;

var logLevelSwitch = new LoggingLevelSwitch();

var startupConfiguration = StartupConfigurationValidator.LoadAndValidate(Directory.GetCurrentDirectory());
var environment = startupConfiguration.Environment;
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

Log.Information("Starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);
    var appDomain = startupConfiguration.AppDomain;

    builder.Services.AddAuthenticationJwtBearer(opts =>
    {
        opts.SigningKey = startupConfiguration.JwtSigningKey;
    });

    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();
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

    var connectionString = startupConfiguration.DatabaseUrl;
    builder.Services.AddDbContextPool<AppDbContext>(opts => opts.UseNpgsql(connectionString)
    );
    Log.Information("Using PostgreSQL database");

    // Custom services
    builder.Services.AddUaDetector();
    builder.Services.AddScoped<ClientToClientWithBalanceDtoMapConfig>();
    builder.Services.AddScoped<ServiceToServiceWithCurrentPriceDtoMapConfig>();
    builder.Services.AddScoped<IAppointmentDeletionService, AppointmentDeletionService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<IEntityFreshnessService, EntityFreshnessService>();
    builder.Services.AddScoped<IRecordActivityService, RecordActivityService>();
    builder.Services.AddScoped<IRequestReplayService, RequestReplayService>();
    builder.Services.AddScoped<IRecurringAppointmentService, RecurringAppointmentService>();
    builder.Services.AddScoped<IRecurringAppointmentMaterializer, RecurringAppointmentMaterializer>();
    builder.Services.AddScoped<IRecurringTaskService, RecurringTaskService>();
    builder.Services.AddScoped<ITeacherScheduleImageGenerator, TeacherScheduleImageGenerator>();
    builder.Services.AddScoped<IUserAvailabilityService, UserAvailabilityService>();

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
    if (environment != "Test")
    {
        builder.Services.AddQuartzServer(q =>
        {
            q.WaitForJobsToComplete = true;
        });
    }

    var app = builder.Build();

    app.UseCors("AllowFrontend");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            if (exception is not null)
            {
                Log.Error(exception, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var detail = environment is "Development" or "Test"
                ? exception?.Message
                : null;

            var problemDetails = ApiErrorResponseFactory.CreateProblemDetails(
                context,
                StatusCodes.Status500InternalServerError,
                detail);

            await context.Response.WriteAsJsonAsync(problemDetails);
        });
    });
    app.UseStatusCodePages(async statusCodeContext =>
    {
        var response = statusCodeContext.HttpContext.Response;
        response.ContentType = "application/problem+json";

        var problemDetails = ApiErrorResponseFactory.CreateProblemDetails(
            statusCodeContext.HttpContext,
            response.StatusCode);

        await response.WriteAsJsonAsync(problemDetails);
    });
    app.UseFastEndpoints(x =>
    {
        x.Errors.UseProblemDetails(pdc =>
            {
                pdc.AllowDuplicateErrors = true;
                pdc.IndicateErrorCode = true;
                pdc.IndicateErrorSeverity = true;
                pdc.TypeValue = "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1";
                pdc.TitleValue = ApiErrorResponseFactory.GetTitle(StatusCodes.Status400BadRequest);
                pdc.TitleTransformer = pd => ApiErrorResponseFactory.GetTitle(pd.Status);
                pdc.ResponseBuilder = ApiErrorResponseFactory.CreateValidationProblemDetails;
            }
        );
        x.Errors.ProducesMetadataType = typeof(ProblemDetails);
        x.Endpoints.ShortNames = true;
        x.Endpoints.Configurator = ep =>
        {
            if (ep.AnonymousVerbs is null)
            {
                ep.PreProcessor<ActiveSessionPreProcessor>(Order.Before);
            }
        };
    });
    app.UseSerilogRequestLogging();
    app.UseSwaggerGen();

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    if (environment != "Test")
    {
        var sql = await File.ReadAllTextAsync(startupConfiguration.QuartzSqlPath);
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    await StartupSeedDataValidator.ValidateAsync(db);

    var superuserRole = await db.Roles.FirstOrDefaultAsync(e => e.RoleName == UserRoles.Superuser);

    var hasSuperuser = await db.Users
        .AsNoTracking()
        .Include(e => e.Role)
        .AnyAsync(e => e.Role == superuserRole!);

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
                Role = superuserRole!,
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

public partial class Program;
