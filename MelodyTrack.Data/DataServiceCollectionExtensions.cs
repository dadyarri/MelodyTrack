using MelodyTrack.Backend.Data;
using MelodyTrack.Core.Security;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Initialization;
using MelodyTrack.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddMelodyTrackData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions(configuration);
        services.AddPersonalDataOptions(configuration);
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();
        services.AddSingleton<CredentialHasher>();
        services.AddSingleton<AuthenticationTokenHasher>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
            options.UseNpgsql(database.ConnectionString, npgsql => npgsql.ConfigureDataSource(dataSourceBuilder =>
            {
                dataSourceBuilder.Name = "melodytrack";
                dataSourceBuilder.EnableParameterLogging(database.EnableSensitiveDataLogging && environment.IsDevelopment());
            }));

            if (database.EnableSensitiveDataLogging && environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
            }
        });
        return services;
    }

    public static IServiceCollection AddMelodyTrackInitialization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInitializationOptions(configuration);
        services.AddScoped<IPersonalDataBackfillService, PersonalDataBackfillService>();
        services.AddScoped<DevelopmentDemoDataSeeder>();
        services.AddScoped<DevelopmentFullDemoDataSeeder>();
        services.AddScoped<DatabaseInitializationService>();
        services.AddScoped<SuperuserRecoveryService>();
        return services;
    }
}
