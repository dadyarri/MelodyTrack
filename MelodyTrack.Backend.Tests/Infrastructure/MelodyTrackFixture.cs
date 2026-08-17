using System.Data;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Data;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Initialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace MelodyTrack.Backend.Tests.Infrastructure;

public sealed class MelodyTrackFixture : AppFixture<Program>
{
    private const string PostgreSqlImage = "postgres:16-alpine@sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50";
    private const string ThrottleBypassHeaderName = "X-Forwarded-For";
    private static readonly SemaphoreSlim ResetLock = new(1, 1);
    private static readonly string[] PreservedTables = ["__EFMigrationsHistory", "Roles", "RecurrenceTypes", "RecurringTaskRules"];

    private PostgreSqlContainer? _dbContainer;

    protected override async ValueTask PreSetupAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        var projectDir = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.FullName;

        _dbContainer = new PostgreSqlBuilder(PostgreSqlImage)
            .WithDatabase("testdb")
            .Build();

        await _dbContainer.StartAsync();

        var connectionString = _dbContainer.GetConnectionString();
        Environment.SetEnvironmentVariable("MELODY_TRACK_DATABASE_URL", connectionString);
        Environment.SetEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY", "super-secret-jwt-key-for-testing-only-1234567890abcdef");
        Environment.SetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY", "super-secret-pii-key-for-testing-only-1234567890abcdef");
        Environment.SetEnvironmentVariable("MELODY_TRACK_APP_DOMAIN", "http://localhost:5000");
        await RunInitializationAsync(InitializationMode.Test, projectDir, connectionString, TestContext.Current.CancellationToken);
    }

    protected override ValueTask SetupAsync()
    {
        Client = CreateClient(new ClientOptions
        {
            ThrottleBypassHeaderName = ThrottleBypassHeaderName
        });

        return ValueTask.CompletedTask;
    }

    protected override void ConfigureApp(IWebHostBuilder app)
    {
        app.UseEnvironment("Test");
        app.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Http:PathBase"] = string.Empty
            });
        });
    }

    public async Task ResetStateAsync(CancellationToken cancellationToken)
    {
        await ResetLock.WaitAsync(cancellationToken);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await ResetDatabaseAsync(db, cancellationToken);
            await SeedBaselineAsync(db, cancellationToken);
            Client.DefaultRequestHeaders.Clear();
        }
        finally
        {
            ResetLock.Release();
        }
    }

    public async Task RunInitializationAsync(InitializationMode mode, CancellationToken cancellationToken)
    {
        var projectDir = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.FullName;
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string is unavailable.");
        await RunInitializationAsync(mode, projectDir, connectionString, cancellationToken);
    }

    protected override async ValueTask TearDownAsync()
    {
        await base.TearDownAsync();

        if (_dbContainer is not null)
        {
            await _dbContainer.StopAsync();
            await _dbContainer.DisposeAsync();
        }
    }

    private static async Task ResetDatabaseAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var tableNames = await GetResettableTableNamesAsync(db, cancellationToken);
        if (tableNames.Count == 0)
        {
            return;
        }

        var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tableNames.Select(name => $"public.\"{name}\""))} RESTART IDENTITY CASCADE;";
        await db.Database.ExecuteSqlRawAsync(truncateSql, cancellationToken);
    }

    private static async Task<List<string>> GetResettableTableNamesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT tablename
                FROM pg_tables
                WHERE schemaname = 'public'
                ORDER BY tablename
                """;

            var tables = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var tableName = reader.GetString(0);
                if (!PreservedTables.Contains(tableName, StringComparer.Ordinal))
                {
                    tables.Add(tableName);
                }
            }

            return tables;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task SeedBaselineAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var superuserRole = await db.Roles
            .FirstAsync(role => role.RoleName == UserRoles.Superuser, cancellationToken);

        await db.InviteCodes.AddAsync(new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = Ulid.NewUlid(),
            Role = superuserRole,
            ValidUntil = DateTime.UtcNow.AddDays(2)
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RunInitializationAsync(
        InitializationMode mode,
        string projectDir,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "MelodyTrack.Init.Tests",
            ContentRootPath = Path.Combine(projectDir, "MelodyTrack.Init"),
            EnvironmentName = "Test"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            ["AuthenticationSecrets:JwtSigningKey"] = "super-secret-jwt-key-for-testing-only-1234567890abcdef",
            ["PersonalData:CurrentKeyVersion"] = "v1",
            ["PersonalData:CurrentKey"] = "super-secret-pii-key-for-testing-only-1234567890abcdef",
            ["PublicUrl:BaseUrl"] = "http://localhost:5000",
            ["Initialization:QuartzSqlPath"] = Path.Combine(projectDir, "MelodyTrack.Init", "quartz.sql")
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthenticationSecretsOptions(builder.Configuration);
        builder.Services.AddPublicUrlOptions(builder.Configuration);
        builder.Services.AddMelodyTrackData(builder.Configuration);
        builder.Services.AddMelodyTrackInitialization(builder.Configuration);

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
        await initializer.RunAsync(mode, cancellationToken);
    }
}
