using System.Data;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace MelodyTrack.Backend.Tests.Infrastructure;

public sealed class MelodyTrackFixture : AppFixture<Program>
{
    private static readonly SemaphoreSlim ResetLock = new(1, 1);
    private static readonly string[] PreservedTables = ["__EFMigrationsHistory", "Roles", "RecurrenceTypes"];

    private string _connectionString = null!;
    private PostgreSqlContainer? _dbContainer;

    protected override async ValueTask PreSetupAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        var projectDir = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.FullName;
        var quartzScriptPath = new FileInfo(Path.Combine(projectDir, "MelodyTrack.Backend", "quartz.sql")).FullName;

        _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithResourceMapping(quartzScriptPath, "/docker-entrypoint-initdb.d")
            .Build();

        await _dbContainer.StartAsync();

        _connectionString = _dbContainer.GetConnectionString();
        Environment.SetEnvironmentVariable("MELODY_TRACK_DATABASE_URL", _connectionString);
        Environment.SetEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY", "super-secret-jwt-key-for-testing-only-1234567890abcdef");
        Environment.SetEnvironmentVariable("MELODY_TRACK_APP_DOMAIN", "http://localhost:5000");
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
}
