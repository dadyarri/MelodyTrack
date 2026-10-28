using System.Text;
using MelodyTrack.Rewrite.Build;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("melodytrack-migrations-");
    private void Write(string name, string content) => File.WriteAllText(Path.Combine(directory.FullName, name), content, new UTF8Encoding(false));

    [Fact]
    public void Load_FilesCreatedOutOfOrder_ReturnsVersionOrder()
    {
        Write("0003_CreateServices.sql", "SELECT 3;");
        Write("0001_CreateClients.sql", "SELECT 1;");
        var migrations = MigrationGeneration.Load(directory.FullName);
        Assert.Equal(new long[] { 1, 3 }, migrations.Select(migration => migration.Version));
        Assert.Equal(new[] { "0001_CreateClients.sql", "0003_CreateServices.sql" }, migrations.Select(migration => migration.Name));
    }
    [Fact]
    public void Load_DuplicateVersions_RejectsMigrations()
    {
        Write("0001_CreateClients.sql", "SELECT 1;");
        Write("0001_CreateServices.sql", "SELECT 2;");
        Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(directory.FullName));
    }
    [Fact]
    public void Load_Utf8BomAndCrLf_HashesOriginalBytes()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("SELECT 1;\r\n")];
        File.WriteAllBytes(Path.Combine(directory.FullName, "0001_CreateClients.sql"), bytes);
        var migration = Assert.Single(MigrationGeneration.Load(directory.FullName));
        Assert.Equal("SELECT 1;\r\n", migration.Sql);
        Assert.Equal("1FFC3C6ACDBB610BCC942C606C787270E7C9D149C473E9D9AAA4FE597D5E402D", migration.Checksum);
    }
    [Theory]
    [InlineData("CreateClients.sql")]
    [InlineData("0000_CreateClients.sql")]
    [InlineData("0001_createClients.sql")]
    [InlineData("0001_Create-Clients.sql")]
    [InlineData("00001_CreateClients.sql")]
    public void Load_InvalidName_RejectsMigration(string name)
    {
        Write(name, "SELECT 1;");
        Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(directory.FullName));
    }
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void Load_EmptySql_RejectsMigration(string sql)
    {
        Write("0001_CreateClients.sql", sql);
        Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(directory.FullName));
    }
    [Fact]
    public void Load_InvalidUtf8_RejectsMigration()
    {
        File.WriteAllBytes(Path.Combine(directory.FullName, "0001_CreateClients.sql"), [0xC3, 0x28]);
        Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(directory.FullName));
    }
    [Fact]
    public void Load_MissingDirectory_RejectsLoad() => Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(Path.Combine(directory.FullName, "missing")));
    [Fact]
    public void Load_EmptyDirectory_ReturnsNoMigrations() => Assert.Empty(MigrationGeneration.Load(directory.FullName));
    [Fact]
    public void Load_MixedKinds_ReturnsSharedVersionOrder()
    {
        Write("0002_UpdateClients.cs", "class UpdateClients { }");
        Write("0001_CreateClients.sql", "SELECT 1;");
        var sources = MigrationGeneration.Load(directory.FullName);
        Assert.Equal(new long[] { 1, 2 }, sources.Select(source => source.Version));
        Assert.Equal("UpdateClients", sources[1].Handler);
    }
    [Fact]
    public void Load_MixedKindsWithSameVersion_RejectsMigrations()
    {
        Write("0001_CreateClients.sql", "SELECT 1;");
        Write("0001_UpdateClients.cs", "class UpdateClients { }");
        Assert.Throws<InvalidMigrationSource>(() => MigrationGeneration.Load(directory.FullName));
    }
    [Fact]
    public void Generate_SourceRemoved_RemovesRegistryEntry()
    {
        Write("0001_CreateClients.sql", "SELECT 1;");
        var output = Path.Combine(directory.FullName, "obj", "GeneratedMigrations.cs");
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        Assert.Contains("0001_CreateClients.sql", File.ReadAllText(output));
        File.Delete(Path.Combine(directory.FullName, "0001_CreateClients.sql"));
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        Assert.DoesNotContain("0001_CreateClients.sql", File.ReadAllText(output));
    }
    [Fact]
    public void Generate_CSharpSourceRemoved_RemovesHandlerReference()
    {
        Write("0001_UpdateClients.cs", "class UpdateClients { }");
        var output = Path.Combine(directory.FullName, "obj", "GeneratedMigrations.cs");
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        Assert.Contains("Migrations.UpdateClients.RunAsync", File.ReadAllText(output));
        File.Delete(Path.Combine(directory.FullName, "0001_UpdateClients.cs"));
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        Assert.DoesNotContain("Migrations.UpdateClients.RunAsync", File.ReadAllText(output));
    }
    [Fact]
    public void Generate_UnchangedSources_DoesNotRewriteRegistry()
    {
        Write("0001_CreateClients.sql", "SELECT 1;");
        var output = Path.Combine(directory.FullName, "obj", "GeneratedMigrations.cs");
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(output, timestamp);
        MigrationGeneration.Generate(directory.FullName, output, "Fixtures");
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(output));
    }

    public void Dispose() => directory.Delete(true);
}
