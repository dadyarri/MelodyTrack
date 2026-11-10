using MelodyTrack.Rewrite.Init.Import;
using MelodyTrack.Rewrite.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MelodyTrack.Rewrite.Init;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Не удалось выполнить Init ({0}).", error.GetType().Name);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Init");
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var cancellationToken = lifetime.ApplicationStopping;
        var exitCode = 1;
        try
        {
            await host.StartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var migrations = GeneratedMigrations.All;
            logger.LogInformation("Загружено миграций: {MigrationCount}.", migrations.Length);
            var connectionString = builder.Configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.LogError("Не задана строка подключения ConnectionStrings:Database.");
            }
            else
            {
                var settings = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Multiplexing = false };
                await using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                await Database.CheckConnectionAsync(connection, cancellationToken);
                await MigrationRunner.PrepareAsync(connection, cancellationToken);
                var pending = await MigrationHistory.GetPendingAsync(connection, migrations, cancellationToken);
                logger.LogInformation("История миграций проверена. Ожидают применения: {MigrationCount}.", pending.Length);
                foreach (var migration in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    logger.LogInformation("Применяется миграция {MigrationName}.", migration.Name);
                    await MigrationRunner.ApplyAsync(connection, migration, logger, cancellationToken);
                    logger.LogInformation("Миграция {MigrationName} применена.", migration.Name);
                }
                logger.LogInformation("Инициализация завершена. Применено миграций: {MigrationCount}.", pending.Length);
                if (builder.Configuration.GetValue<bool>("Auth:Bootstrap"))
                    exitCode = await AuthBootstrapCommand.RunAsync(builder.Configuration, new Database(dataSource), cancellationToken);
                else
                    exitCode = builder.Configuration.GetValue<bool>("Import:Clients")
                        ? await ClientImportCommand.RunAsync(builder.Configuration, connection, logger, cancellationToken)
                        : 0;
            }
        }
        catch (MigrationLockUnavailableException) { logger.LogError("Другой Init уже выполняет инициализацию этой базы данных."); }
        catch (InvalidMigrationException error) { logger.LogError("Ошибка миграций: {Reason}", error.Message); }
        catch (MigrationFailedException error)
        {
            if (error.InnerException is PostgresException postgres)
                logger.LogError("Ошибка миграции {MigrationName}. PostgreSQL SQLSTATE: {SqlState}.", error.MigrationName, postgres.SqlState);
            else logger.LogError("Ошибка миграции {MigrationName} ({ErrorType}).", error.MigrationName, error.InnerException?.GetType().Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Инициализация отменена.");
            exitCode = 130;
        }
        catch (Exception error) { logger.LogError("Не удалось выполнить Init ({ErrorType}).", error.GetType().Name); }
        finally { await host.StopAsync(); }
        return exitCode;
    }
}
