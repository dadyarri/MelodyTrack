using MelodyTrack.Common.Data;
using MelodyTrack.Common.Utils;
using MelodyTrack.LegacyDataMigrator.OldData;
using MelodyTrack.MigratorService;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<AppV1DbContext>(options =>
    options.UseNpgsql(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODYTRACK_V1_DATABASE_URL")));

builder.AddNpgsqlDbContext<AppDbContext>(connectionName: "melodytrack");

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();