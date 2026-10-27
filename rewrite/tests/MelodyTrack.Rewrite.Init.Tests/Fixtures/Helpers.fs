module MelodyTrack.Rewrite.Init.Tests.Fixtures.Helpers

open MelodyTrack.Rewrite.Init
open Microsoft.Extensions.Logging

let insert (context: MigrationContext) =
    context.CancellationToken.ThrowIfCancellationRequested()
    context.Logger.LogInformation("Тестовая F#-миграция вызывает внешний модуль.")
    SqlMigration.run "INSERT INTO generated_probe (value) VALUES ('F# через внешний модуль');" context
