using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.MigratorService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migrating database...");

        using var scope = _scopeFactory.CreateScope();
        var newDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quartzScript = await File.ReadAllTextAsync("quartz.sql", stoppingToken);

        await newDb.Database.MigrateAsync(stoppingToken);
        await newDb.Database.ExecuteSqlRawAsync(quartzScript, stoppingToken);

        _hostApplicationLifetime.StopApplication();
    }
}