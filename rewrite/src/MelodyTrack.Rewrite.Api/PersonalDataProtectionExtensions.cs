using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Rewrite.Api;

/// <summary>Регистрирует защиту данных нового хранилища без доступа к ключам исходной БД.</summary>
public static class PersonalDataProtectionExtensions
{
    /// <summary>Подключает ключи из конфигурации с обязательной проверкой при старте host.</summary>
    /// <param name="services">Регистрации API.</param>
    /// <returns>Та же коллекция сервисов.</returns>
    public static IServiceCollection AddPersonalDataProtection(this IServiceCollection services)
    {
        services.AddOptions<PersonalDataProtectionOptions>()
            .BindConfiguration(PersonalDataProtectionOptions.SectionName)
            .Validate(ValidKeys, "Некорректная конфигурация PersonalData: требуется текущая версия и 256-битные Base64-ключи.")
            .ValidateOnStart();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PersonalDataProtectionOptions>>().Value;
            return new PersonalDataProtector(options.CurrentKeyVersion, options.Keys);
        });
        services.AddSingleton<ClientDataProtection>();
        return services;
    }

    private static bool ValidKeys(PersonalDataProtectionOptions options)
    {
        try
        {
            using var protector = new PersonalDataProtector(options.CurrentKeyVersion, options.Keys);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
