using System.Collections.Immutable;
using System.Data;
using Dapper;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

/// <summary>
/// Неизменяемое описание SQL-запроса с именованными параметрами и типизированными методами чтения.
/// </summary>
/// <remarks>
/// <para>
/// Builder создаётся через <see cref="Database.Query"/> или <see cref="DatabaseTransaction.Query"/>.
/// Вызов <see cref="WithParameter"/> возвращает новую ветку и сохраняет привязку к транзакционной области.
/// SQL выполняется только терминальными методами <c>ExecuteAsync</c> и <c>Get*Async</c>.
/// </para>
/// <para>
/// При каждом выполнении создаётся собственный набор параметров Dapper. Обычный запрос получает
/// отдельное соединение; транзакционный использует соединение своей области. Результаты читаются
/// полностью до возврата, поэтому их можно использовать после освобождения соединения.
/// </para>
/// <para>
/// Сопоставление столбцов свойствам или конструктору модели выполняет Dapper. При необходимости
/// задавайте имена столбцов через SQL aliases. Для сложного доменного объекта сначала получите
/// проекцию строки, затем вызовите доменную фабрику.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var query = database.Query(selectSql);
/// var active = query.WithParameter("archived", false);
/// var archived = query.WithParameter("archived", true);
///
/// var clients = await active.GetAllAsync&lt;ClientRow&gt;(cancellationToken);
/// </code>
/// </example>
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

    /// <summary>
    /// Возвращает новую ветку запроса с добавленной или заменённой привязкой параметра.
    /// </summary>
    /// <param name="name">
    /// Имя без префикса либо с одним начальным <c>@</c>. Допустимы латинские буквы, цифры
    /// и подчёркивание; имя не должно начинаться с цифры.
    /// </param>
    /// <param name="value">Значение параметра, включая <see langword="null"/> для SQL NULL.</param>
    /// <param name="type">
    /// Необязательный тип ADO.NET. Если не задан, тип определяет Dapper; явный тип полезен
    /// для значений <see langword="null"/>. Для отображаемого PostgreSQL enum оставьте null:
    /// его тип и метки определяются через <see cref="DatabaseEnums.MapDomainEnums"/>.
    /// </param>
    /// <returns>Новый builder; исходный набор привязок не изменяется.</returns>
    /// <remarks>
    /// Значение передаётся отдельно от SQL. Повторное имя заменяет значение только в новой ветке;
    /// <c>id</c> и <c>@id</c> обозначают одну привязку. Имена таблиц и столбцов нельзя передавать
    /// как параметры значений. Для типизированного идентификатора передавайте его <c>Value</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Имя параметра пусто/недопустимо либо для PostgreSQL enum задан DbType.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Значение отображаемого enum не определено.</exception>
    /// <example>
    /// <code>
    /// await database.Query(updateSql)
    ///     .WithParameter("id", clientId.Value)
    ///     .WithParameter("notes", null, DbType.String)
    ///     .ExecuteAsync(cancellationToken);
    /// </code>
    /// </example>
    public DatabaseQuery WithParameter(string name, object? value, DbType? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.StartsWith('@') ? name[1..] : name;
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_')
            || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Имя параметра должно содержать буквы, цифры и подчёркивания и не начинаться с цифры.", nameof(name));
        }

        return new DatabaseQuery(database, sql, parameters.SetItem(key, new(DatabaseEnums.ParameterValue(value, type), type)), transaction);
    }

    /// <summary>
    /// Выполняет SQL-команду без возврата данных и количества изменённых строк.
    /// </summary>
    /// <param name="cancellationToken">Отмена получения соединения и выполнения команды.</param>
    /// <returns>Асинхронная операция выполнения команды.</returns>
    /// <remarks>
    /// Подходит для INSERT, UPDATE, DELETE и DDL без возвращаемых данных. Для команд с RETURNING
    /// используйте один из методов <c>Get*Async</c>. В транзакционной области выполнение команды
    /// не фиксирует транзакцию: commit выполняется отдельно.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Выполнение отменено.</exception>
    /// <exception cref="InvalidOperationException">Связанная транзакция уже завершена.</exception>
    /// <exception cref="ObjectDisposedException">Источник данных или транзакционная область освобождены.</exception>
    /// <exception cref="NpgsqlException">Ошибка подключения или выполнения SQL.</exception>
    /// <example>
    /// <code>
    /// await database.Query("UPDATE clients SET notes = @notes WHERE id = @id")
    ///     .WithParameter("id", clientId.Value)
    ///     .WithParameter("notes", notes)
    ///     .ExecuteAsync(cancellationToken);
    /// </code>
    /// </example>
    public Task ExecuteAsync(CancellationToken cancellationToken) => RunAsync(
        (connection, command) => connection.ExecuteAsync(command), cancellationToken);

    /// <summary>
    /// Читает все строки запроса и преобразует каждую строку в указанный тип.
    /// </summary>
    /// <typeparam name="T">Тип проекции строки или простого значения, поддерживаемый Dapper.</typeparam>
    /// <param name="cancellationToken">Отмена получения соединения и чтения данных.</param>
    /// <returns>Полностью прочитанный список, доступный через интерфейс только для чтения; при отсутствии строк — пустой.</returns>
    /// <remarks>
    /// Метод использует буферизованное чтение. Перечисление возвращённого списка больше не обращается к БД
    /// и не удерживает reader. Порядок строк определяется SQL; для стабильного порядка задайте ORDER BY.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Чтение отменено.</exception>
    /// <exception cref="InvalidOperationException">Связанная транзакция уже завершена.</exception>
    /// <exception cref="ObjectDisposedException">Источник данных или транзакционная область освобождены.</exception>
    /// <exception cref="NpgsqlException">Ошибка подключения или выполнения SQL.</exception>
    /// <exception cref="DataException">Не удалось преобразовать данные строки в модель.</exception>
    /// <example>
    /// <code>
    /// var clients = await database.Query(selectSql)
    ///     .WithParameter("archived", false)
    ///     .GetAllAsync&lt;ClientRow&gt;(cancellationToken);
    /// </code>
    /// </example>
    public Task<IReadOnlyList<T>> GetAllAsync<T>(CancellationToken cancellationToken) => RunAsync<IReadOnlyList<T>>(
        async (connection, command) => (await connection.QueryAsync<T>(command)).AsList(), cancellationToken);

    /// <summary>
    /// Читает ровно одну строку запроса и преобразует её в указанный тип.
    /// </summary>
    /// <typeparam name="T">Тип модели строки либо простого значения.</typeparam>
    /// <param name="cancellationToken">Отмена получения соединения и чтения данных.</param>
    /// <returns>Единственная строка, преобразованная в <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// Подходит для выборки обязательной записи, агрегатного значения или INSERT с RETURNING.
    /// В отличие от получения первой строки, метод проверяет, что второй строки нет.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Запрос вернул ноль или несколько строк либо связанная транзакция завершена.</exception>
    /// <exception cref="OperationCanceledException">Чтение отменено.</exception>
    /// <exception cref="ObjectDisposedException">Источник данных или транзакционная область освобождены.</exception>
    /// <exception cref="NpgsqlException">Ошибка подключения или выполнения SQL.</exception>
    /// <exception cref="DataException">Не удалось преобразовать данные строки в модель.</exception>
    /// <example>
    /// <code>
    /// var client = await transaction.Query(selectSql)
    ///     .WithParameter("id", clientId.Value)
    ///     .GetSingleAsync&lt;ClientRow&gt;(cancellationToken);
    ///
    /// var count = await database.Query("SELECT count(*) FROM clients")
    ///     .GetSingleAsync&lt;long&gt;(cancellationToken);
    /// </code>
    /// </example>
    public Task<T> GetSingleAsync<T>(CancellationToken cancellationToken) => RunAsync(
        (connection, command) => connection.QuerySingleAsync<T>(command), cancellationToken);

    /// <summary>
    /// Читает не более одной строки либо возвращает значение по умолчанию при её отсутствии.
    /// </summary>
    /// <typeparam name="T">Тип модели строки либо простого значения.</typeparam>
    /// <param name="cancellationToken">Отмена получения соединения и чтения данных.</param>
    /// <returns>Полученная строка или <c>default(T)</c>, если строк нет.</returns>
    /// <remarks>
    /// Для ссылочного типа отсутствие строки означает <see langword="null"/>. Для значимого типа
    /// возвращается его значение по умолчанию, например ноль для <see cref="int"/>. Если отсутствие
    /// нужно отличать от нуля, укажите nullable-тип, например <c>int?</c>. Несколько строк считаются ошибкой.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Запрос вернул несколько строк либо связанная транзакция завершена.</exception>
    /// <exception cref="OperationCanceledException">Чтение отменено.</exception>
    /// <exception cref="ObjectDisposedException">Источник данных или транзакционная область освобождены.</exception>
    /// <exception cref="NpgsqlException">Ошибка подключения или выполнения SQL.</exception>
    /// <exception cref="DataException">Не удалось преобразовать данные строки в модель.</exception>
    /// <example>
    /// <code>
    /// var client = await database.Query(selectSql)
    ///     .WithParameter("id", clientId.Value)
    ///     .GetSingleOrDefaultAsync&lt;ClientRow&gt;(cancellationToken);
    ///
    /// var optionalNumber = await database.Query("SELECT 0 WHERE false")
    ///     .GetSingleOrDefaultAsync&lt;int?&gt;(cancellationToken); // null
    /// </code>
    /// </example>
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
