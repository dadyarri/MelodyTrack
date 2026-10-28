using MelodyTrack.Rewrite.Init;
using Microsoft.Extensions.Logging;

namespace MelodyTrack.Rewrite.Init.Tests.Fixtures;

internal static class Helpers
{
    internal static Task InsertAsync(MigrationContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.Logger.LogInformation("Тестовая C#-миграция вызывает внешний модуль.");
        return SqlMigration.RunAsync("INSERT INTO generated_probe (value) VALUES ('C# через внешний модуль');", context);
    }
}
