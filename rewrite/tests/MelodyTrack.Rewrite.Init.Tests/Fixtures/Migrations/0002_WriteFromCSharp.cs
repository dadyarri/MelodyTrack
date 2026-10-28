using MelodyTrack.Rewrite.Init;

namespace MelodyTrack.Rewrite.Init.Tests.Fixtures.Migrations;

internal static class WriteFromCSharp
{
    internal static Task RunAsync(MigrationContext context) => Helpers.InsertAsync(context);
}
