using FastEndpoints.Testing;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Testcontainers.PostgreSql;

namespace MelodyTrack.Backend.Tests;

public class MelodyTrackFixture : AppFixture<Program>
{
    private string _connectionString = null!;
    private PostgreSqlContainer? _dbContainer;
    private ISchedulerFactory? _schedulerFactory;

    protected override async ValueTask PreSetupAsync()
    {

        var projectDir = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.FullName;
        var quartzScriptPath = new FileInfo(Path.Combine(projectDir, "MelodyTrack.Backend", "quartz.sql")).FullName;

        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:latest")
            .WithDatabase(database: "testdb")
            .WithPortBinding(5432, true)
            .WithResourceMapping(quartzScriptPath, "/docker-entrypoint-initdb.d")
            .Build();

        await _dbContainer.StartAsync();

        _connectionString = _dbContainer.GetConnectionString();
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        base.ConfigureApp(a);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("MELODY_TRACK_DATABASE_URL", _connectionString);
        Environment.SetEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY", "super-secret-jwt-key-for-testing-only-1234567890abcdef");
        Environment.SetEnvironmentVariable("MELODY_TRACK_APP_DOMAIN", "http://localhost:5000");
    }

    protected override void ConfigureServices(IServiceCollection s)
    {
        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connectionString));
    }

    protected override ValueTask SetupAsync()
    {
        _schedulerFactory = Services.GetRequiredService<ISchedulerFactory>();

        return base.SetupAsync();
    }

    protected override async ValueTask TearDownAsync()
    {
        if (_dbContainer is not null)
        {
            await _dbContainer.StopAsync();
            await _dbContainer.DisposeAsync();
        }

        if (_schedulerFactory is not null)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            await scheduler.Shutdown(true);
        }
    }
}