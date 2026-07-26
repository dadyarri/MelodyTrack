using System.IO.Compression;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend;
using MelodyTrack.Backend.Api.Auth.PreProcessors;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Jobs;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using NJsonSchema;
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
    builder.Services.AddApiRateLimiting();
    builder.Services.AddSingleton(startupConfiguration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddFastEndpoints(x => { x.SourceGeneratorDiscoveredTypes = DiscoveredTypes.All; });
    builder.Services.AddSerilog();
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            ["application/problem+json"]);
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Melody Track API";
            s.Version = "v2";
            s.DocumentName = "v2";
            s.PostProcess = document =>
            {
                ConfigureOpenApiContract(document);
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
    builder.Services.AddSingleton<IPersonalDataProtector>(_ =>
        new PersonalDataProtector(startupConfiguration.PiiMasterKeyVersion, startupConfiguration.PiiMasterKeys));
    builder.Services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(connectionString)
    );
    Log.Information("Using PostgreSQL database");

    // Custom services
    builder.Services.AddUaDetector();
    builder.Services.AddScoped<ClientToClientWithBalanceDtoMapConfig>();
    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
    builder.Services.AddScoped<ServiceToServiceWithCurrentPriceDtoMapConfig>();
    builder.Services.AddScoped<IAppointmentDeletionService, AppointmentDeletionService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<CourseProgressService>();
    builder.Services.AddScoped<IEntityFreshnessService, EntityFreshnessService>();
    builder.Services.AddScoped<IPersonalDataBackfillService, PersonalDataBackfillService>();
    builder.Services.AddScoped<IRecordActivityService, RecordActivityService>();
    builder.Services.AddScoped<IRequestReplayService, RequestReplayService>();
    builder.Services.AddSingleton<IPublicUrlBuilder, PublicUrlBuilder>();
    builder.Services.AddScoped<IRecurringAppointmentService, RecurringAppointmentService>();
    builder.Services.AddScoped<IRecurringAppointmentMaterializer, RecurringAppointmentMaterializer>();
    builder.Services.AddScoped<IRecurringTaskService, RecurringTaskService>();
    builder.Services.AddScoped<IRecurringTaskCandidateService, RecurringTaskCandidateService>();
    builder.Services.AddScoped<IRecurringTaskTransitionService, RecurringTaskTransitionService>();
    builder.Services.AddScoped<IRecurringTaskQueryService, RecurringTaskQueryService>();
    builder.Services.AddScoped<ICustomTaskTransitionService, CustomTaskTransitionService>();
    builder.Services.AddSingleton<IRecurringTaskTemplateRenderer, RecurringTaskTemplateRenderer>();
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

    app.UseFastEndpoints(x =>
    {
        x.Errors.UseProblemDetails(pdc =>
            {
                pdc.AllowDuplicateErrors = false;
                pdc.IndicateErrorCode = true;
                pdc.IndicateErrorSeverity = false;
                pdc.TypeValue = ApiProblemTypes.Validation;
                pdc.TitleValue = ApiErrorResponseFactory.GetTitle(StatusCodes.Status400BadRequest);
                pdc.TitleTransformer = pd => ApiErrorResponseFactory.GetTitle(pd.Status);
                pdc.ResponseBuilder = ApiErrorResponseFactory.CreateValidationProblemDetails;
            }
        );
        x.Errors.ContentType = ApiMediaTypes.ProblemJson;
        x.Errors.ProducesMetadataType = typeof(ApiProblemDetails);
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
    app.UseResponseCompression();
    app.UseCors("AllowFrontend");
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            headers.TryAdd("X-Trace-Id", context.TraceIdentifier);

            if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
                && context.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                context.Response.ContentType = ApiMediaTypes.ProblemJson;
            }

            if (ShouldDisableCaching(context.Request.Path) || headers.ContainsKey("Content-Disposition"))
            {
                headers["Cache-Control"] = "no-store, no-cache, max-age=0";
                headers["Pragma"] = "no-cache";
                headers["Expires"] = "0";
            }

            return Task.CompletedTask;
        });

        await next();
    });
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            if (exception is RequestReplayConflictException replayConflict)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/problem+json";
                var conflictDetails = ApiErrorResponseFactory.CreateProblemDetails(
                    context,
                    StatusCodes.Status409Conflict,
                    replayConflict.Message,
                    ApiProblemCodes.IdempotencyConflict,
                    ApiProblemTypes.IdempotencyConflict);
                await conflictDetails.ExecuteAsync(context);
                return;
            }

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

            await problemDetails.ExecuteAsync(context);
        });
    });
    app.Use(async (context, next) =>
    {
        await next();

        var response = context.Response;
        if (response.StatusCode < StatusCodes.Status400BadRequest
            || response.HasStarted
            || response.ContentLength is > 0)
        {
            return;
        }

        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            response.Headers.WWWAuthenticate = "Bearer";
        }

        var problemDetails = ApiErrorResponseFactory.CreateProblemDetails(
            context,
            response.StatusCode);

        await problemDetails.ExecuteAsync(context);
    });
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseSwaggerGen();

    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publicUrlBuilder = scope.ServiceProvider.GetRequiredService<IPublicUrlBuilder>();
        var nowUtc = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        await db.Database.MigrateAsync();
        var personalDataBackfillService = scope.ServiceProvider.GetRequiredService<IPersonalDataBackfillService>();
        await personalDataBackfillService.BackfillAsync(CancellationToken.None);

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
            .FirstOrDefaultAsync(e => e.Role == superuserRole && !e.WasUsed && e.ValidUntil >= nowUtc);

        if (!hasSuperuser)
        {
            InviteCode bootstrapInvite;
            if (inviteCode is null)
            {
                bootstrapInvite = new InviteCode
                {
                    Id = Ulid.NewUlid(),
                    Code = Ulid.NewUlid(),
                    Role = superuserRole!,
                    ValidUntil = nowUtc.AddDays(2)
                };
                await db.InviteCodes.AddAsync(bootstrapInvite);
                await db.SaveChangesAsync();
            }
            else
            {
                bootstrapInvite = inviteCode;
            }

            await db.AuditLogs.AddAsync(new AuditLog
            {
                Id = Ulid.NewUlid(),
                CreatedAtUtc = nowUtc,
                Category = "security",
                Action = "superuser_bootstrap_invite_available",
                EntityType = "invite",
                EntityId = bootstrapInvite.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Приглашение", UserUtils.DescribeInviteCodeForLogs(bootstrapInvite.Code)),
                    AuditDetailsFormatter.DescribeContext("Действует до", bootstrapInvite.ValidUntil))
            });
            await db.SaveChangesAsync();

            var inviteRef = UserUtils.DescribeInviteCodeForLogs(bootstrapInvite.Code);
            if (startupConfiguration.LogBootstrapSecrets)
            {
                var url = publicUrlBuilder.GetInviteUrl(bootstrapInvite.Code);
                Log.Warning("Superuser was not created yet. Bootstrap invite {InviteRef} can be used at {Link}", inviteRef, url);
            }
            else
            {
                Log.Warning(
                    "Superuser was not created yet. Bootstrap invite {InviteRef} exists until {ValidUntilUtc:O}. Full link logging is disabled; enable {EnvironmentVariable}=true only for controlled recovery.",
                    inviteRef,
                    bootstrapInvite.ValidUntil,
                    "MELODY_TRACK_LOG_BOOTSTRAP_SECRETS");
            }
        }
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

static bool ShouldDisableCaching(PathString path)
{
    if (path.StartsWithSegments("/auth"))
    {
        return true;
    }

    return path.StartsWithSegments("/users", out var remainingPath)
           && remainingPath.Value?.EndsWith("/password-reset-links", StringComparison.OrdinalIgnoreCase) == true;
}

static void ConfigureOpenApiContract(OpenApiDocument document)
{
    if (!document.Components.Schemas.TryGetValue(nameof(ApiProblemDetails), out var problemSchema))
    {
        throw new InvalidOperationException($"OpenAPI generation did not register {nameof(ApiProblemDetails)}.");
    }

    foreach (var description in document.Operations)
    {
        var operation = description.Operation;
        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            operation.OperationId = CreateOperationId(description.Method, description.Path);
        }

        EnsureProblemResponse(operation, problemSchema, StatusCodes.Status405MethodNotAllowed);
        if (operation.Security is { Count: > 0 })
        {
            EnsureProblemResponse(operation, problemSchema, StatusCodes.Status401Unauthorized);
            EnsureProblemResponse(operation, problemSchema, StatusCodes.Status403Forbidden);
        }
        if (operation.RequestBody is not null)
        {
            EnsureProblemResponse(operation, problemSchema, StatusCodes.Status400BadRequest);
            EnsureProblemResponse(operation, problemSchema, StatusCodes.Status415UnsupportedMediaType);
        }

        foreach (var responseEntry in operation.Responses.ToArray())
        {
            if (!int.TryParse(responseEntry.Key, out var statusCode) || statusCode < StatusCodes.Status400BadRequest)
            {
                AddTraceHeader(responseEntry.Value);
                continue;
            }

            ConfigureProblemResponse(responseEntry.Value, problemSchema, statusCode);
        }

        if (operation.Responses.TryGetValue(StatusCodes.Status201Created.ToString(), out var createdResponse))
        {
            createdResponse.Headers["Location"] = new OpenApiHeader
            {
                Description = "URI of the created resource"
            };

            if (description.Method.Equals("post", StringComparison.OrdinalIgnoreCase)
                && SupportsIdempotency(description.Path)
                && operation.Parameters.All(parameter => !parameter.Name.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
            {
                operation.Parameters.Add(new OpenApiHeader
                {
                    Name = "Idempotency-Key",
                    Description = "Optional caller-chosen key. Reusing it with the same payload replays the completed creation; a different payload returns 409.",
                    IsRequired = false,
                    Kind = OpenApiParameterKind.Header
                });
            }
        }

        if (GetDownloadMediaType(description.Path) is { } downloadMediaType)
        {
            if (!operation.Responses.TryGetValue(StatusCodes.Status200OK.ToString(), out var downloadResponse))
            {
                downloadResponse = new OpenApiResponse { Description = "Download" };
                operation.Responses[StatusCodes.Status200OK.ToString()] = downloadResponse;
            }
            downloadResponse.Content.Clear();
            downloadResponse.Content[downloadMediaType] = new OpenApiMediaType
            {
                Schema = new JsonSchema
                {
                    Type = JsonObjectType.String,
                    Format = "binary"
                }
            };
            AddTraceHeader(downloadResponse);
        }

        foreach (var successResponseEntry in operation.Responses.Where(entry => int.TryParse(entry.Key, out var code) && code is >= 200 and < 300))
        {
            var response = successResponseEntry.Value;
            if (response.Content.Keys.Any(mediaType => mediaType is not "application/json" and not ApiMediaTypes.ProblemJson))
            {
                response.Headers["Content-Disposition"] = new OpenApiHeader
                {
                    Description = "Attachment filename using standard filename encoding"
                };
                response.Headers["Cache-Control"] = new OpenApiHeader
                {
                    Description = "Download caching policy; generated and private downloads use no-store"
                };
            }
        }

        if (!operation.Responses.TryGetValue(StatusCodes.Status500InternalServerError.ToString(), out var serverErrorResponse))
        {
            serverErrorResponse = new OpenApiResponse
            {
                Description = ApiErrorResponseFactory.GetTitle(StatusCodes.Status500InternalServerError)
            };
            operation.Responses[StatusCodes.Status500InternalServerError.ToString()] = serverErrorResponse;
        }
        ConfigureProblemResponse(serverErrorResponse, problemSchema, StatusCodes.Status500InternalServerError);
    }
}

static void EnsureProblemResponse(OpenApiOperation operation, JsonSchema problemSchema, int statusCode)
{
    var responseKey = statusCode.ToString();
    if (!operation.Responses.TryGetValue(responseKey, out var response))
    {
        response = new OpenApiResponse();
        operation.Responses[responseKey] = response;
    }
    ConfigureProblemResponse(response, problemSchema, statusCode);
}

static void ConfigureProblemResponse(OpenApiResponse response, JsonSchema problemSchema, int statusCode)
{
    response.Description = ApiErrorResponseFactory.GetTitle(statusCode);
    response.Content.Clear();
    response.Content[ApiMediaTypes.ProblemJson] = new OpenApiMediaType
    {
        Schema = new JsonSchema { Reference = problemSchema }
    };
    AddTraceHeader(response);

    if (statusCode == StatusCodes.Status401Unauthorized)
    {
        response.Headers["WWW-Authenticate"] = new OpenApiHeader
        {
            Description = "Bearer authentication challenge"
        };
    }

    if (statusCode is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable)
    {
        response.Headers["Retry-After"] = new OpenApiHeader
        {
            Description = "Delay in seconds or an HTTP date when retry timing is known"
        };
    }
}

static void AddTraceHeader(OpenApiResponse response)
{
    response.Headers["X-Trace-Id"] = new OpenApiHeader
    {
        Description = "Request trace identifier also returned in Problem Details"
    };
}

static string CreateOperationId(string method, string path)
{
    var normalizedPath = string.Join(
        '_',
        path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim('{', '}').Replace('-', '_')));
    return $"{method.ToLowerInvariant()}_{normalizedPath}";
}

static bool SupportsIdempotency(string path) => path is
    "/appointments"
    or "/clients"
    or "/payments"
    or "/expenses"
    or "/course-enrollments"
    or "/courses"
    or "/services"
    or "/expense-categories"
    or "/client-sources";

static string? GetDownloadMediaType(string path) => path switch
{
    "/exports/client-debts" or "/exports/expenses" or "/exports/payments" =>
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "/exports/teacher-schedule" => "image/png",
    "/calendar-subscriptions/{token}.ics" => "text/calendar",
    _ => null
};

public partial class Program;
