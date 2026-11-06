using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Import;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MelodyTrack.Rewrite.Init.Import;

public static class ClientImportCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, NpgsqlConnection target, ILogger logger, CancellationToken token)
    {
        var connectionString = configuration.GetConnectionString("OldDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError("Не задана строка подключения ConnectionStrings:OldDatabase.");
            return 1;
        }
        using var legacy = new LegacyPersonalDataReader(ReadKeys(configuration, "Import:LegacyKeys"));
        using var protector = new PersonalDataProtector(configuration["PersonalData:CurrentKeyVersion"] ?? "",
            ReadKeys(configuration, "PersonalData:Keys"));
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Multiplexing = false, IncludeErrorDetail = false };
        settings.Options = (settings.Options + " -c default_transaction_read_only=on").Trim();
        await using var source = new NpgsqlConnection(settings.ConnectionString);
        await source.OpenAsync(token);
        var report = await ClientImport.RunAsync(source, target, legacy, new ClientDataProtection(protector), token);
        logger.LogInformation("Импорт клиентов: источников {SourceRows}, клиентов {ClientRows}, строк контактов {ContactRows}. Вставлено источников {SourcesInserted}, клиентов {ClientsInserted}, контактов {ContactsInserted}. Совпало источников {SourcesMatched}, клиентов {ClientsMatched}.",
            report.SourceRows, report.ClientRows, report.ContactRows, report.SourcesInserted, report.ClientsInserted, report.ContactsInserted, report.SourcesMatched, report.ClientsMatched);
        foreach (var group in report.Issues.GroupBy(issue => issue.Code))
            logger.LogWarning("Импорт требует ручного разбора: {IssueCode}, строк/полей: {IssueCount}.", group.Key, group.Count());
        return report.Complete ? 0 : 2;
    }

    private static Dictionary<string, string> ReadKeys(IConfiguration configuration, string section) =>
        configuration.GetSection(section).GetChildren().ToDictionary(child => child.Key, child => child.Value ?? "", StringComparer.Ordinal);
}
