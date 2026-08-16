using MelodyTrack.Backend.Data;
using MelodyTrack.Core.Security;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Initialization;
using MelodyTrack.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddMelodyTrackData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions(configuration);
        services.AddPersonalDataOptions(configuration);
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(database.ConnectionString);
        });
        return services;
    }

    public static IServiceCollection AddMelodyTrackInitialization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInitializationOptions(configuration);
        services.AddScoped<IPersonalDataBackfillService, PersonalDataBackfillService>();
        services.AddScoped<DatabaseInitializationService>();
        return services;
    }
}
