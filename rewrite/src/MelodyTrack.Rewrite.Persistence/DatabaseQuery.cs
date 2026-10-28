using System.Collections.Immutable;
using System.Data;
using Dapper;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

public sealed class DatabaseQuery
{
    private sealed record Parameter(object? Value, DbType? Type);

    private readonly Database database;
    private readonly string sql;
    private readonly ImmutableDictionary<string, Parameter> parameters;
    private readonly DatabaseTransaction? transaction;

    internal DatabaseQuery(Database database, string sql)
        : this(database, sql, ImmutableDictionary<string, Parameter>.Empty, null)
    {
    }

    internal DatabaseQuery(Database database, string sql, DatabaseTransaction transaction)
        : this(database, sql, ImmutableDictionary<string, Parameter>.Empty, transaction)
    {
    }

    private DatabaseQuery(Database database, string sql, ImmutableDictionary<string, Parameter> parameters, DatabaseTransaction? transaction)
    {
        this.database = database;
        this.sql = sql;
        this.parameters = parameters;
        this.transaction = transaction;
    }

    public DatabaseQuery WithParameter(string name, object? value, DbType? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.StartsWith('@') ? name[1..] : name;
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_')
            || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Имя параметра должно содержать буквы, цифры и подчёркивания и не начинаться с цифры.", nameof(name));
        }

        return new DatabaseQuery(database, sql, parameters.SetItem(key, new(value, type)), transaction);
    }

    public Task ExecuteAsync(CancellationToken cancellationToken) => RunAsync(
        (connection, command) => connection.ExecuteAsync(command), cancellationToken);

    public Task<IReadOnlyList<T>> GetAllAsync<T>(CancellationToken cancellationToken) => RunAsync<IReadOnlyList<T>>(
        async (connection, command) => (await connection.QueryAsync<T>(command)).AsList(), cancellationToken);

    public Task<T> GetSingleAsync<T>(CancellationToken cancellationToken) => RunAsync(
        (connection, command) => connection.QuerySingleAsync<T>(command), cancellationToken);

    public Task<T?> GetSingleOrDefaultAsync<T>(CancellationToken cancellationToken) => RunAsync(
        (connection, command) => connection.QuerySingleOrDefaultAsync<T>(command), cancellationToken);

    private Task<T> RunAsync<T>(Func<NpgsqlConnection, CommandDefinition, Task<T>> operation, CancellationToken cancellationToken)
    {
        Task<T> Execute(NpgsqlConnection connection, NpgsqlTransaction? currentTransaction, CancellationToken token)
        {
            var values = new DynamicParameters();
            foreach (var (name, parameter) in parameters)
            {
                values.Add(name, parameter.Value, parameter.Type);
            }

            var command = new CommandDefinition(sql, values, transaction: currentTransaction, flags: CommandFlags.Buffered, cancellationToken: token);
            return operation(connection, command);
        }

        if (transaction is not null)
        {
            return transaction.RunAsync(Execute, cancellationToken);
        }

        return database.WithConnectionAsync((connection, token) => Execute(connection, null, token), cancellationToken);
    }
}
