using System.IO.Compression;
using System.Reflection;
using MelodyTrack.Backend;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth;
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
using MelodyTrack.Backend.GodMode;
using MelodyTrack.Backend.Jobs;
using MelodyTrack.Backend.Notifications;
using MelodyTrack.Backend.OpenApi;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Backend.Validation;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data;
using MelodyTrack.Data.Configuration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using Scalar.AspNetCore;
using UaDetector;
using HttpOptions = MelodyTrack.Backend.Configuration.HttpOptions;

var logLevelSwitch = new LoggingLevelSwitch();
var loggerProviders = new LoggerProviderCollection();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(LegacyConfiguration.ReadEnvironmentAliases());
var isOpenApiGeneration = string.Equals(
    Assembly.GetEntryAssembly()?.GetName().Name,
    "GetDocument.Insider",
    StringComparison.Ordinal);
if (isOpenApiGeneration)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{AuthenticationSecretsOptions.SectionName}:JwtSigningPrivateKey"] = "base64:MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg1a+XfTTbRx+lAZXtBVgkgxPy4juOyvu9VuwfrFCy9BihRANCAATHVVdEpzPvwGWCKZ7kcmGIqi6JGlxlaa6/mELjK19tAuNSLWWbhxeWb0LaVYdquLVhzFnyWL1XsTRPxSen4PvA",
        [$"{AuthenticationSecretsOptions.SectionName}:PasswordPepper"] = "base64:G2UfJdjsXXVuK72YyyE+thhGeWP+luj3S6ifPMqjZtA=",
        [$"{AuthenticationSecretsOptions.SectionName}:PortalPinPepper"] = "base64:VFWWTyDfkCqiB2TC7OrIQpT8FyXZRCuALw2YJbQDcPw=",
        [$"{AuthenticationSecretsOptions.SectionName}:RefreshTokenHashKey"] = "base64:5sXZ/oCgEMjrXA1KzQGzAkN88oDl4GZS6gefagjMjW4=",
        [$"{AuthenticationSecretsOptions.SectionName}:CsrfSigningKey"] = "base64:NWgzsvzLSMFqAg08Nh5+7TE7dbd/paept2GeaGandu0=",
        [$"{JwtOptions.SectionName}:Issuer"] = "MelodyTrack",
        [$"{JwtOptions.SectionName}:Audience"] = "MelodyTrack.Web",
        [$"{PersonalDataOptions.SectionName}:CurrentKey"] = "openapi-generation-key-not-used-at-runtime-1234567890",
        [$"{DatabaseOptions.SectionName}:ConnectionString"] = "Host=localhost;Database=openapi;Username=openapi;Password=openapi",
        [$"{PublicUrlOptions.SectionName}:BaseUrl"] = "https://localhost",
        [$"{GodModeOptions.SectionName}:PublicBaseUrl"] = "https://localhost:8081",
        [$"{GodModeOptions.SectionName}:SessionSigningKey"] = "base64:VotRvCQQSz26pgRuUrZEknXSxlUpkTASdZSpNAi+aBQ="
    });
}
builder.AddServiceDefaults("melodytrack-backend");
// Serilog owns console rendering; the remaining Microsoft providers receive structured events for export.
builder.Services.RemoveAll<ConsoleLoggerProvider>();
var releaseChangelog = ReleaseChangelog.Load(FindReleaseDirectory());
var environment = builder.Environment.EnvironmentName;
logLevelSwitch.MinimumLevel = environment == "Development"
    ? LogEventLevel.Debug
    : LogEventLevel.Information;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .MinimumLevel.ControlledBy(logLevelSwitch)
    .WriteTo.Providers(loggerProviders)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)
    .CreateLogger();

Log.Information(
    "{StartupBanner:l}",
    StartupBanner.Render(releaseChangelog.Current.Version, releaseChangelog.Current.ResolvedCodename));

try
{
    var authenticationSecrets = builder.Configuration
        .GetRequiredSection(AuthenticationSecretsOptions.SectionName)
        .Get<AuthenticationSecretsOptions>()
        ?? throw new InvalidOperationException("Authentication secrets are not configured.");
    var jwtOptions = builder.Configuration
        .GetRequiredSection(JwtOptions.SectionName)
        .Get<JwtOptions>()
        ?? throw new InvalidOperationException("JWT options are not configured.");
    var personalDataKey = builder.Configuration[$"{PersonalDataOptions.SectionName}:CurrentKey"] ?? string.Empty;
    UserUtils.ConfigureAuthentication(authenticationSecrets, jwtOptions, personalDataKey);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = JwtKeyMaterial.CreateValidationKey(authenticationSecrets.JwtSigningPrivateKey),
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddDatabaseAuthorization();
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
    builder.Services.AddOptions<GodModeOptions>()
        .Bind(builder.Configuration.GetSection(GodModeOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(
            options => Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
            "GodMode:PublicBaseUrl must be an absolute HTTPS URL.")
        .ValidateOnStart();
    builder.Services.AddOptions<WebPushOptions>()
        .Bind(builder.Configuration.GetSection(WebPushOptions.SectionName))
        .Validate(options =>
                !options.Enabled ||
                Uri.TryCreate(options.Subject, UriKind.Absolute, out _) &&
                !string.IsNullOrWhiteSpace(options.PublicKey) &&
                !string.IsNullOrWhiteSpace(options.PrivateKey),
            "WebPush requires an absolute subject and a separate VAPID public/private key pair when enabled.")
        .ValidateOnStart();
    builder.Services.AddMelodyTrackData(builder.Configuration);
    builder.Services.AddValidation();
    builder.Services.AddProblemDetails(options =>
    {
        options.CustomizeProblemDetails = context =>
        {
            var statusCode = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            context.ProblemDetails.Status = statusCode;
            context.ProblemDetails.Type = ApiProblemTypes.ForStatus(statusCode);
            context.ProblemDetails.Title = ApiErrorResponseFactory.GetTitle(statusCode);
            context.ProblemDetails.Detail ??= ApiErrorResponseFactory.GetDefaultDetail(statusCode);
            context.ProblemDetails.Instance = context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions["code"] = ApiProblemCodes.ForStatus(statusCode);
            context.ProblemDetails.Extensions["traceId"] = ApiTraceContext.GetTraceId(context.HttpContext);
            context.ProblemDetails.Extensions["errors"] = Array.Empty<ApiValidationError>();
        };
    });
    builder.Services.AddOpenApi("v1", options =>
    {
        options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;
        options.CreateSchemaReferenceId = typeInfo =>
            (Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type) == typeof(Ulid)
            ? null
            : Microsoft.AspNetCore.OpenApi.OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);
        options.AddDocumentTransformer<MelodyTrackOpenApiTransformer>();
        options.AddOperationTransformer<MelodyTrackOpenApiTransformer>();
        options.AddSchemaTransformer<MelodyTrackOpenApiTransformer>();
    });
    builder.Services.AddSerilog(Log.Logger, dispose: false, providers: loggerProviders);
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
    // Database configuration

    var connectionString = builder.Configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
    Log.Information("Using PostgreSQL database");

    // Custom services
    builder.Services.AddUaDetector();
    builder.Services.AddScoped<ActiveSessionValidator>();
    builder.Services.AddScoped<ApiValidationErrorCollection>();
    builder.Services.AddSingleton<ICommonPasswordService, CommonPasswordService>();
    builder.Services.AddScoped<ClientWithBalanceDtoMapper>();
    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
    builder.Services.AddScoped<ServiceWithCurrentPriceDtoMapper>();
    builder.Services.AddScoped<IAppointmentDeletionService, AppointmentDeletionService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddSingleton<GodModeAccessService>();
    builder.Services.AddScoped<RefreshSessionCookieService>();
    builder.Services.AddSingleton<JwtTokenService>();
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
    builder.Services.AddScoped<IVacationRequestWorkflowService, VacationRequestWorkflowService>();
    builder.Services.AddScoped<IVacationRequestQueryService, VacationRequestQueryService>();
    builder.Services.AddScoped<IVacationRequestSubjectLock, VacationRequestSubjectLock>();
    builder.Services.AddScoped<IWorkingHoursRequestWorkflowService, WorkingHoursRequestWorkflowService>();
    builder.Services.AddScoped<IWorkingHoursRequestQueryService, WorkingHoursRequestQueryService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddSingleton<NotificationTelemetry>();
    builder.Services.AddSingleton<WebPush.WebPushClient>();
    if (environment != "Test" && !isOpenApiGeneration)
    {
        builder.Services.AddHostedService<PushDeliveryWorker>();
    }

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
    if (environment != "Test" && !isOpenApiGeneration)
    {
        builder.Services.AddQuartzServer(q =>
        {
            q.WaitForJobsToComplete = true;
        });
    }

    var app = builder.Build();
    var httpOptions = app.Services.GetRequiredService<IOptions<HttpOptions>>().Value;
    var godModeOptions = app.Services.GetRequiredService<IOptions<GodModeOptions>>().Value;
    _ = MelodyTrack.Data.Security.AuthenticationSecretMaterial.DecodeSymmetricKey(
        godModeOptions.SessionSigningKey,
        "GodMode:SessionSigningKey");

    app.UseTrustedReverseProxy();
    app.UseGodModeListenerIsolation(godModeOptions);

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

            if (exception is not null and not BadHttpRequestException)
            {
                Log.Error(exception, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                new ProblemDetailsContext { HttpContext = context });
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

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
            new ProblemDetailsContext { HttpContext = context });
    });
    app.UseSpaStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.MapDefaultEndpoints();
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
    }
    var apiEndpoints = app.MapGroup(httpOptions.PathBase);
    apiEndpoints.RequireAuthorization(AuthorizationPolicies.ApiAccess);
    apiEndpoints.AddEndpointFilter<NativeValidationEndpointFilter>();
    apiEndpoints.MapGeneratedApiEndpoints();
    app.MapGodModeEndpoints();
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

public partial class Program;
