using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MelodyTrack.Rewrite.Persistence;

/// <summary>
/// Регистрирует сервисы доступа к базе данных в стандартном DI-контейнере .NET.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="Database"/> как singleton, использующий источник данных из DI.
    /// </summary>
    /// <param name="services">Коллекция регистраций приложения.</param>
    /// <returns>Та же коллекция для продолжения цепочки регистраций.</returns>
    /// <remarks>
    /// <para>
    /// К моменту разрешения <see cref="Database"/> в контейнере должен быть зарегистрирован
    /// <see cref="Npgsql.NpgsqlDataSource"/>. Существующая регистрация <see cref="Database"/>
    /// сохраняется; повторный вызов не добавляет её дубликат. Метод не открывает соединений.
    /// </para>
    /// <para>
    /// Источник данных, созданный фабрикой регистрации из примера, освобождает DI-контейнер.
    /// Обёртка остаётся общей для приложения, а открытые соединения и транзакции имеют собственный
    /// ограниченный срок жизни.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> равен <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddSingleton&lt;NpgsqlDataSource&gt;(_ =&gt;
    ///     NpgsqlDataSource.Create(connectionString));
    /// builder.Services.AddDatabase();
    ///
    /// var app = builder.Build();
    /// var database = app.Services.GetRequiredService&lt;Database&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        services.TryAddSingleton<Database>();
        return services;
    }
}
