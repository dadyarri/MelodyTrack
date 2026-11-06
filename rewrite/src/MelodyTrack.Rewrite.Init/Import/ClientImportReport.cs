namespace MelodyTrack.Rewrite.Init.Import;

// RowNumber is the 1-based position in the source table ordered by its bytea ID.
// Reports deliberately exclude names, contact values, ciphertext and source identifiers.
public sealed record ClientImportIssue(string Table, int RowNumber, string? Field, string Code);

public sealed class ClientImportReport
{
    public int SourceRows { get; internal set; }
    public int ClientRows { get; internal set; }
    public int ContactRows { get; internal set; }
    public int SourcesInserted { get; internal set; }
    public int SourcesMatched { get; internal set; }
    public int ClientsInserted { get; internal set; }
    public int ClientsMatched { get; internal set; }
    public int ContactsInserted { get; internal set; }
    public List<ClientImportIssue> Issues { get; } = [];
    public bool Complete => Issues.Count == 0;
}
