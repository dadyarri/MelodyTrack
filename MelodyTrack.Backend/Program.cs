using System.IO.Compression;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.Backend;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Auth.PreProcessors;
using MelodyTrack.Backend.Api.ClientPortal;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Api.Onboarding;
using MelodyTrack.Backend.Api.Reports.Reporting;
using MelodyTrack.Backend.Api.Schedule;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Configuration;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Hosting;
using MelodyTrack.Backend.Jobs;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data;
using MelodyTrack.Data.Configuration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(LegacyConfiguration.ReadEnvironmentAliases());
builder.AddServiceDefaults();
var releaseChangelog = ReleaseChangelog.Load(FindReleaseDirectory());
var environment = builder.Environment.EnvironmentName;
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

Log.Information(
    "{StartupBanner:l}",
    StartupBanner.Render(releaseChangelog.Current.Version, releaseChangelog.Current.ResolvedCodename));

try
{
    var jwtSigningKey = builder.Configuration[$"{AuthenticationSecretsOptions.SectionName}:JwtSigningKey"] ?? string.Empty;
    var personalDataKey = builder.Configuration[$"{PersonalDataOptions.SectionName}:CurrentKey"] ?? string.Empty;
    UserUtils.ConfigureLegacySecrets(jwtSigningKey, personalDataKey);

    builder.Services.AddAuthenticationJwtBearer(opts =>
    {
        opts.SigningKey = jwtSigningKey;
    });

    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddApiRateLimiting();
    builder.Services.AddSingleton(releaseChangelog);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddAuthenticationSecretsOptions(builder.Configuration);
    builder.Services.AddPublicUrlOptions(builder.Configuration);
    builder.Services.AddTrustedReverseProxy(builder.Configuration);
    builder.Services.AddOptions<HttpOptions>()
        .Bind(builder.Configuration.GetSection(HttpOptions.SectionName))
        .Validate(
            options => string.IsNullOrEmpty(options.PathBase)
                       || options.PathBase.StartsWith('/') && !options.PathBase.EndsWith('/'),
            "Http:PathBase must be empty or start with '/' and must not end with '/'.")
        .ValidateOnStart();
    builder.Services.AddMelodyTrackData(builder.Configuration);
    builder.Services.AddFastEndpoints(DiscoveredTypes.All);
    builder.Services.AddSerilog();
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            ["application/manifest+json", "application/problem+json", "application/wasm"]);
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    var configuredApiPathBase = builder.Configuration[$"{HttpOptions.SectionName}:PathBase"] ?? string.Empty;
    builder.Services.SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Melody Track API";
            s.Version = "v2";
            s.DocumentName = "v2";
            s.PostProcess = document =>
            {
                ConfigureOpenApiContract(document, configuredApiPathBase);
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
    // Database configuration

    var connectionString = builder.Configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
    Log.Information("Using PostgreSQL database");

    // Custom services
    builder.Services.AddUaDetector();
    builder.Services.AddScoped<ClientToClientWithBalanceDtoMapConfig>();
    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
    builder.Services.AddScoped<ServiceToServiceWithCurrentPriceDtoMapConfig>();
    builder.Services.AddScoped<IAppointmentDeletionService, AppointmentDeletionService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<RefreshSessionCookieService>();
    builder.Services.AddScoped<SessionSecurityMonitor>();
    builder.Services.AddScoped<AppointmentUpdatePreparationService>();
    builder.Services.AddScoped<CourseProgressService>();
    builder.Services.AddScoped<ClientPortalSessionService>();
    builder.Services.AddScoped<IEntityFreshnessService, EntityFreshnessService>();
    builder.Services.AddScoped<OnboardingStateService>();
    builder.Services.AddScoped<IRecordActivityService, RecordActivityService>();
    builder.Services.AddScoped<IRequestReplayService, RequestReplayService>();
    builder.Services.AddScoped<IPersonalDashboardQueryService, PersonalDashboardQueryService>();
    builder.Services.AddScoped<IReportContextFactory, ReportContextFactory>();
    builder.Services.AddScoped<IReportAppointmentQuery, ReportAppointmentQuery>();
    builder.Services.AddScoped<IWorkReportQueryService, WorkReportQueryService>();
    builder.Services.AddScoped<IFinanceReportQueryService, FinanceReportQueryService>();
    builder.Services.AddScoped<IClientsReportQueryService, ClientsReportQueryService>();
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
    var httpOptions = app.Services.GetRequiredService<IOptions<HttpOptions>>().Value;

    app.UseTrustedReverseProxy();

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
        if (!string.IsNullOrEmpty(httpOptions.PathBase))
        {
            x.Endpoints.RoutePrefix = httpOptions.PathBase.Trim('/');
        }
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
    app.UseUnifiedRuntimeHeaders(httpOptions.PathBase);
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
    app.UseSpaStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseSwaggerGen();
    app.MapDefaultEndpoints();
    app.MapSpaFallback();

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

static string FindReleaseDirectory()
{
    var workingDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "changelog", "releases");
    return Directory.Exists(workingDirectoryPath)
        ? workingDirectoryPath
        : Path.Combine(AppContext.BaseDirectory, "changelog", "releases");
}

static void ConfigureOpenApiContract(OpenApiDocument document, string apiPathBase)
{
    if (!document.Components.Schemas.TryGetValue(nameof(ApiProblemDetails), out var problemSchema))
    {
        throw new InvalidOperationException($"OpenAPI generation did not register {nameof(ApiProblemDetails)}.");
    }

    foreach (var description in document.Operations)
    {
        var operation = description.Operation;
        var contractPath = RemoveApiPathBase(description.Path, apiPathBase);
        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            operation.OperationId = CreateOperationId(description.Method, contractPath);
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
                && SupportsIdempotency(contractPath)
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

        if (GetDownloadMediaType(contractPath) is { } downloadMediaType)
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

static string RemoveApiPathBase(string path, string apiPathBase)
{
    var normalizedPathBase = apiPathBase.TrimEnd('/');
    return !string.IsNullOrEmpty(normalizedPathBase)
           && path.StartsWith($"{normalizedPathBase}/", StringComparison.OrdinalIgnoreCase)
        ? path[normalizedPathBase.Length..]
        : path;
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
