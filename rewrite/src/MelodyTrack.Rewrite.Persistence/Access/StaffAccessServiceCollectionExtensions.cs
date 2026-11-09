using MelodyTrack.Rewrite.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Registers current database authorization without introducing an authentication bypass.</summary>
public static class StaffAccessServiceCollectionExtensions
{
    /// <summary>Requires Init schema 0008 and trusted account provisioning before staff access becomes available.</summary>
    public static IServiceCollection AddCurrentStaffAccess(this IServiceCollection services)
    {
        services.AddDatabase();
        services.TryAddSingleton<StaffPolicyStore>();
        services.TryAddSingleton<IStaffPolicyStore>(provider => provider.GetRequiredService<StaffPolicyStore>());
        services.TryAddSingleton<ICurrentStaffAccess>(provider => provider.GetRequiredService<StaffPolicyStore>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
