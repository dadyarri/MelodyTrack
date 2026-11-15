using System.Globalization;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Persistence.Clients;

/// <summary>Читает список клиентов без заметок, финансов и неподтверждённых этапов отношений.</summary>
public sealed class ClientDirectoryStore(Database database, ClientDataProtection protection) : IClientDirectoryStore
{
    /// <summary>Фильтрует защищённые контакты до подсчёта и пагинации в согласованном снимке.</summary>
    public async Task<ClientDirectoryPage> ReadAsync(ClientDirectoryQuery query, CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        await transaction.Query("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY").ExecuteAsync(cancellationToken);
        var rows = await transaction.Query("""
            SELECT c.id AS "Id", c.first_name AS "FirstName", c.last_name AS "LastName", c.middle_name AS "MiddleName",
                   c.birthdate::text AS "Birthdate", c.source_id AS "SourceId", s.name AS "SourceName",
                   c.is_archived AS "IsArchived", c.created_at AS "CreatedAt",
                   CASE
                     WHEN search.query = '' THEN true
                     WHEN strpos(search.full_name, search.query) > 0 THEN true
                     WHEN char_length(search.full_name) <= 255 AND char_length(search.query) <= 255
                       AND abs(char_length(search.full_name) - char_length(search.query)) <= 3
                     THEN public.levenshtein_less_equal(search.full_name, search.query, 3) <= 3
                     ELSE false
                   END AS "NameMatches"
            FROM public.clients c LEFT JOIN public.client_sources s ON s.id = c.source_id
            CROSS JOIN LATERAL (
                SELECT lower(btrim(regexp_replace(concat_ws(' ', c.last_name, c.first_name, c.middle_name), '[[:space:]]+', ' ', 'g'))) AS full_name,
                       lower(btrim(regexp_replace(COALESCE(@q, ''), '[[:space:]]+', ' ', 'g'))) AS query
            ) search
            WHERE (@archive = 'all' OR c.is_archived = (@archive = 'archived'))
              AND (@sourceId IS NULL OR c.source_id = @sourceId)
              AND (NOT @withoutSource OR c.source_id IS NULL)
            """)
            .WithParameter("archive", query.Archive)
            .WithParameter("q", query.Q, System.Data.DbType.String)
            .WithParameter("sourceId", query.SourceId, System.Data.DbType.Guid)
            .WithParameter("withoutSource", query.WithoutSource)
            .GetAllAsync<ClientRow>(cancellationToken);

        IReadOnlyList<ContactRow> contactRows = [];
        Dictionary<Guid, ClientDirectoryContact[]>? searchable = null;
        if (!string.IsNullOrWhiteSpace(query.Q) && rows.Count > 0)
        {
            contactRows = await ReadContactsAsync(transaction, rows.Select(row => row.Id).ToArray(), cancellationToken);
            searchable = DecryptContacts(contactRows, cancellationToken);
            rows = rows.Where(row => Matches(row, searchable.GetValueOrDefault(row.Id, []), query.Q, cancellationToken)).ToArray();
        }
        var ordered = rows.ToArray();
        var sorts = query.ReadSorts().Value;
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(ordered, (left, right) => Compare(left, right, sorts));
        cancellationToken.ThrowIfCancellationRequested();
        var total = ordered.Length;
        var pageRows = ordered.Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, total)).Take(query.PageSize).ToArray();
        if (searchable is null && pageRows.Length > 0)
            contactRows = await ReadContactsAsync(transaction, pageRows.Select(row => row.Id).ToArray(), cancellationToken);
        var contacts = searchable ?? DecryptContacts(contactRows, cancellationToken);
        var items = pageRows.Select(row => new ClientDirectoryEntry(row.Id, row.FirstName, row.LastName, row.MiddleName,
            row.Birthdate is { } birthdate ? DateOnly.ParseExact(birthdate, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            row.SourceId, row.SourceName, row.IsArchived, new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            contacts.GetValueOrDefault(row.Id, []))).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new(items, query.Page, query.PageSize, total, (int)Math.Ceiling((double)total / query.PageSize));
    }

    /// <summary>Возвращает справочники фильтров, включая архивные виды исторических контактов.</summary>
    public async Task<ClientDirectoryOptions> ReadOptionsAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        await transaction.Query("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY").ExecuteAsync(cancellationToken);
        var sources = await transaction.Query("SELECT id AS \"Id\", name AS \"Name\" FROM public.client_sources ORDER BY name, id")
            .GetAllAsync<ClientDirectorySource>(cancellationToken);
        var kinds = await transaction.Query("SELECT id AS \"Id\", name AS \"Name\", format::text AS \"Format\", is_archived AS \"IsArchived\" FROM public.contact_kinds ORDER BY name, id")
            .GetAllAsync<ClientDirectoryContactKind>(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(sources, kinds);
    }

    private static Task<IReadOnlyList<ContactRow>> ReadContactsAsync(DatabaseTransaction transaction, Guid[] ids,
        CancellationToken cancellationToken) => transaction.Query("""
            SELECT cc.id AS "Id", cc.client_id AS "ClientId", cc.kind_id AS "KindId", k.name AS "KindName",
                   k.format::text AS "Format", cc.protected_value AS "Value", cc.protected_comment AS "Comment", cc.is_primary AS "IsPrimary"
            FROM public.client_contacts cc JOIN public.contact_kinds k ON k.id = cc.kind_id
            WHERE cc.client_id = ANY(@ids) ORDER BY k.name, k.id, cc.is_primary DESC, cc.id
            """).WithParameter("ids", ids).GetAllAsync<ContactRow>(cancellationToken);

    private Dictionary<Guid, ClientDirectoryContact[]> DecryptContacts(IReadOnlyList<ContactRow> rows, CancellationToken token)
    {
        var values = new Dictionary<Guid, List<ClientDirectoryContact>>();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var id = new ClientId(row.ClientId);
            var contactId = new ClientContactId(row.Id);
            if (!values.TryGetValue(row.ClientId, out var contacts)) values.Add(row.ClientId, contacts = []);
            contacts.Add(new(row.Id, row.KindId, row.KindName, row.Format, protection.UnprotectValue(id, contactId, row.Value),
                protection.UnprotectComment(id, contactId, row.Comment), row.IsPrimary));
        }
        return values.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static bool Matches(ClientRow row, ClientDirectoryContact[] contacts, string query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (row.NameMatches) return true;
        var digits = new string(query.Where(char.IsDigit).ToArray());
        var phoneQuery = digits.Length >= 3 && query.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || "+()- .".Contains(character));
        return contacts.Any(contact => contact.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
            || phoneQuery && contact.Format == "phone" && new string(contact.Value.Where(char.IsDigit).ToArray()).Contains(digits, StringComparison.Ordinal));
    }

    private static int Compare(ClientRow left, ClientRow right, IReadOnlyList<ClientDirectorySort> sorts)
    {
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);
        foreach (var sort in sorts)
        {
            var direction = sort.Descending ? -1 : 1;
            int Nullable<T>(T? first, T? second, Func<T, T, int> compare) where T : class =>
                first is null ? second is null ? 0 : 1 : second is null ? -1 : direction * compare(first, second);
            var result = sort.Key switch
            {
                "status" => direction * left.IsArchived.CompareTo(right.IsArchived),
                "createdAt" => direction * left.CreatedAt.CompareTo(right.CreatedAt),
                "source" => Nullable(left.SourceName, right.SourceName, comparer.Compare),
                "birthdate" => Nullable(left.Birthdate, right.Birthdate, StringComparer.Ordinal.Compare),
                _ => direction * CompareName(left, right, comparer)
            };
            if (result != 0) return result;
        }
        return left.Id.CompareTo(right.Id);
    }

    private static int CompareName(ClientRow left, ClientRow right, StringComparer comparer)
    {
        var result = comparer.Compare(left.LastName, right.LastName);
        if (result == 0) result = comparer.Compare(left.FirstName, right.FirstName);
        return result == 0 ? comparer.Compare(left.MiddleName, right.MiddleName) : result;
    }

    private sealed record ClientRow(Guid Id, string FirstName, string LastName, string? MiddleName, string? Birthdate,
        Guid? SourceId, string? SourceName, bool IsArchived, DateTime CreatedAt, bool NameMatches)
    {
        public override string ToString() => nameof(ClientRow);
    }
    private sealed record ContactRow(Guid Id, Guid ClientId, Guid KindId, string KindName, string Format, string Value,
        string? Comment, bool IsPrimary)
    {
        public override string ToString() => nameof(ContactRow);
    }
}
