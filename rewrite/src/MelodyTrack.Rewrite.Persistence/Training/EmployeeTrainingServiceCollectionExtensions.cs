using MelodyTrack.Rewrite.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MelodyTrack.Rewrite.Persistence.Training;

/// <summary>Registers progress persistence without inventing the host's current-content access evaluator.</summary>
public static class EmployeeTrainingServiceCollectionExtensions
{
    /// <summary>The host must register IEmployeeTrainingAccess with its actual scenario/article authorization.</summary>
    public static IServiceCollection AddEmployeeTrainingProgress(this IServiceCollection services)
    {
        services.AddDatabase();
        services.TryAddSingleton<IEmployeeTrainingStore, EmployeeTrainingStore>();
        services.TryAddScoped<EmployeeTrainingService>();
        return services;
    }
}
