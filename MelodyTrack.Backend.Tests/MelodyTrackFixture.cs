using FastEndpoints.Testing;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace MelodyTrack.Backend.Tests;

public class MelodyTrackFixture : AppFixture<Program>
{
    private PostgreSqlContainer? _dbContainer;
    private string _connectionString = null!;

    protected override async ValueTask PreSetupAsync()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:latest")
            .WithDatabase(database: "testdb")
            .WithPortBinding(5432, true)
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

    protected override async ValueTask TearDownAsync()
    {
        if (_dbContainer is not null)
        {
            await _dbContainer.StopAsync();
            await _dbContainer.DisposeAsync();
        }
    }
}