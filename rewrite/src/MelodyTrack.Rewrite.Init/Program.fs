module MelodyTrack.Rewrite.Init.Program

open System
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open MelodyTrack.Rewrite.Persistence
open Npgsql

let private run args =
    task {
        let settings = HostApplicationBuilderSettings(Args = args, ContentRootPath = AppContext.BaseDirectory)
        let builder = Host.CreateApplicationBuilder(settings)
        use host = builder.Build()
        let logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Init")
        let lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>()

        let! exitCode =
            task {
                try
                    do! host.StartAsync()
                    lifetime.ApplicationStopping.ThrowIfCancellationRequested()
                    let migrations = GeneratedMigrations.all
                    logger.LogInformation("Загружено миграций: {MigrationCount}.", migrations.Length)
                    let connectionString = builder.Configuration.GetConnectionString("Database")

                    if String.IsNullOrWhiteSpace(connectionString) then
                        logger.LogError("Не задана строка подключения ConnectionStrings:Database.")
                        return 1
                    else
                        let databaseSettings = NpgsqlConnectionStringBuilder(connectionString)
                        databaseSettings.Pooling <- false
                        databaseSettings.Multiplexing <- false
                        use dataSource = NpgsqlDataSource.Create(databaseSettings.ConnectionString)
                        use! connection = dataSource.OpenConnectionAsync(lifetime.ApplicationStopping)
                        do! Database.checkConnection connection lifetime.ApplicationStopping
                        do! MigrationRunner.prepare connection lifetime.ApplicationStopping
                        let! pending = MigrationHistory.getPending connection migrations lifetime.ApplicationStopping
                        logger.LogInformation("История миграций проверена. Ожидают применения: {MigrationCount}.", pending.Length)

                        for migration in pending do
                            lifetime.ApplicationStopping.ThrowIfCancellationRequested()
                            logger.LogInformation("Применяется миграция {MigrationName}.", migration.Name)
                            do! MigrationRunner.apply connection migration logger lifetime.ApplicationStopping
                            logger.LogInformation("Миграция {MigrationName} применена.", migration.Name)

                        logger.LogInformation("Инициализация завершена. Применено миграций: {MigrationCount}.", pending.Length)
                        return 0
                with
                | MigrationLockUnavailable ->
                    logger.LogError("Другой Init уже выполняет инициализацию этой базы данных.")
                    return 1
                | InvalidMigration message ->
                    logger.LogError("Ошибка миграций: {Reason}", message)
                    return 1
                | :? MigrationFailedException as error ->
                    match error.InnerException with
                    | :? PostgresException as postgresError ->
                        logger.LogError("Ошибка миграции {MigrationName}. PostgreSQL SQLSTATE: {SqlState}.", error.MigrationName, postgresError.SqlState)
                    | cause ->
                        logger.LogError("Ошибка миграции {MigrationName} ({ErrorType}).", error.MigrationName, cause.GetType().Name)

                    return 1
                | :? OperationCanceledException when lifetime.ApplicationStopping.IsCancellationRequested ->
                    logger.LogInformation("Инициализация отменена.")
                    return 130
                | error ->
                    logger.LogError("Не удалось выполнить Init ({ErrorType}).", error.GetType().Name)
                    return 1
            }

        do! host.StopAsync()
        return exitCode
    }

[<EntryPoint>]
let main args =
    try
        (run args).GetAwaiter().GetResult()
    with error ->
        Console.Error.WriteLine("Не удалось выполнить Init ({0}).", error.GetType().Name)
        1
