using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Migrator;
using MelodyTrack.Migrator.OldData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((_, services) =>
    {
        services.AddDbContext<AppV1DbContext>(opts =>
        {
            opts.UseNpgsql(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODYTRACK_V1_DATABASE_URL"));
        });
        services.AddDbContext<AppDbContext>(opts =>
        {
            opts.UseNpgsql(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODYTRACK_V2_DATABASE_URL"));
        });
        services.AddSingleton<IHostedService, MigratorHostedService>();
    });

await host.Build().RunAsync();