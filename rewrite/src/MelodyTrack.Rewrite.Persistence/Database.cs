using Dapper;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

/// <summary>
/// Выполняет параметризованные SQL-запросы и создаёт транзакционные области на основе источника данных из DI.
/// </summary>
/// <remarks>
/// <para>
/// Сервис не хранит открытое соединение или текущую транзакцию и может регистрироваться как singleton.
/// Обычные запросы получают отдельное соединение на время выполнения; транзакционная область владеет
/// одним соединением до своего освобождения.
/// </para>
/// <para>
/// Источником данных владеет создавший его код или DI-контейнер. Этот сервис его не освобождает.
/// Для обычного SQL используйте <see cref="Query"/>, для атомарной группы запросов —
/// <see cref="BeginTransactionAsync"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var clients = await database.Query(sql)
///     .WithParameter("archived", false)
///     .GetAllAsync&lt;ClientRow&gt;(cancellationToken);
/// </code>
/// </example>
public sealed class Database
{
    private readonly NpgsqlDataSource dataSource;

    /// <summary>
    /// Создаёт обёртку над общим источником данных; соединение при этом не открывается.
    /// </summary>
    /// <param name="dataSource">Источник данных, обычно предоставляемый DI как singleton.</param>
    public Database(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    /// <summary>
    /// Создаёт неизменяемое описание SQL-запроса с автоматическим управлением соединением.
    /// </summary>
    /// <param name="sql">SQL с именованными параметрами, например <c>WHERE id = @id</c>.</param>
    /// <returns>Builder, к которому можно добавить параметры и выбрать способ получения результата.</returns>
    /// <remarks>
    /// Создание builder не выполняет SQL. Каждый вызов <c>ExecuteAsync</c> или <c>Get*Async</c>
    /// открывает соединение и освобождает его после чтения результата или исключения.
    /// Запрос не присоединяется автоматически к транзакциям других запросов.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">SQL пуст или содержит только пробелы.</exception>
    /// <example>
    /// <code>
    /// var client = await database.Query(selectSql)
    ///     .WithParameter("id", clientId.Value)
    ///     .GetSingleOrDefaultAsync&lt;ClientRow&gt;(cancellationToken);
    /// </code>
    /// </example>
    public DatabaseQuery Query(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new DatabaseQuery(this, sql);
    }

    /// <summary>
    /// Открывает соединение и начинает транзакцию для нескольких fluent-запросов.
    /// </summary>
    /// <param name="cancellationToken">Отмена открытия соединения и начала транзакции.</param>
    /// <returns>Область, которую вызывающий код должен освободить через <c>await using</c>.</returns>
    /// <remarks>
    /// <para>
    /// Создавайте запросы через <see cref="DatabaseTransaction.Query"/>, чтобы они использовали
    /// общее соединение и транзакцию. Запросы одной области выполняются последовательно.
    /// </para>
    /// <para>
    /// Изменения сохраняются только после явного <see cref="DatabaseTransaction.CommitAsync"/>.
    /// Выход из области без commit, включая исключение или ранний возврат, откатывает изменения.
    /// Если начало транзакции завершается ошибкой, уже открытое соединение освобождается.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Операция отменена.</exception>
    /// <exception cref="ObjectDisposedException">Источник данных уже освобождён.</exception>
    /// <exception cref="NpgsqlException">Не удалось открыть соединение или начать транзакцию.</exception>
    /// <example>
    /// <code>
    /// await using (var transaction = await database.BeginTransactionAsync(cancellationToken))
    /// {
    ///     await transaction.Query(updateSql)
    ///         .WithParameter("id", clientId.Value)
    ///         .WithParameter("notes", notes)
    ///         .ExecuteAsync(cancellationToken);
    ///
    ///     var client = await transaction.Query(selectSql)
    ///         .WithParameter("id", clientId.Value)
    ///         .GetSingleAsync&lt;ClientRow&gt;(cancellationToken);
    ///
    ///     await transaction.CommitAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    public async Task<DatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new DatabaseTransaction(this, connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Выполняет низкоуровневую операцию на автоматически открытом соединении и затем освобождает его.
    /// </summary>
    /// <typeparam name="T">Тип полностью полученного результата операции.</typeparam>
    /// <param name="operation">Операция, получающая открытое соединение и токен отмены.</param>
    /// <param name="cancellationToken">Отмена открытия соединения; тот же токен передаётся операции.</param>
    /// <returns>Результат <paramref name="operation"/> после завершения операции.</returns>
    /// <remarks>
    /// <para>
    /// Это служебный механизм выполнения обычных запросов builder. Для прикладного SQL используйте
    /// <see cref="Query"/>, а для транзакций — <see cref="BeginTransactionAsync"/>.
    /// Прямой вызов нужен только для операций, которым необходим доступ к Npgsql.
    /// </para>
    /// <para>
    /// Операция должна передавать полученный токен в свои вызовы ввода-вывода и завершить чтение
    /// до возврата. Нельзя возвращать reader или ленивую последовательность, зависящие от соединения:
    /// после завершения callback соединение уже освобождается. Метод не создаёт транзакцию
    /// и не выполняет commit автоматически.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Открытие соединения или операция отменены.</exception>
    /// <exception cref="NpgsqlException">Ошибка подключения или выполнения команды Npgsql.</exception>
    /// <example>
    /// <code>
    /// var value = await database.WithConnectionAsync(async (connection, token) =&gt;
    /// {
    ///     await using var command = new NpgsqlCommand("SELECT 42", connection);
    ///     return (int)(await command.ExecuteScalarAsync(token))!;
    /// }, cancellationToken);
    /// </code>
    /// </example>
    internal async Task<T> WithConnectionAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await operation(connection, cancellationToken);
    }

    /// <summary>
    /// Проверяет возможность выполнения команд на переданном соединении запросом <c>SELECT 1</c>.
    /// </summary>
    /// <param name="connection">Соединение, жизненным циклом которого управляет вызывающий код.</param>
    /// <param name="cancellationToken">Отмена проверочного запроса.</param>
    /// <returns>Асинхронная операция, успешно завершающаяся при доступности базы данных.</returns>
    /// <remarks>
    /// Используется Init для проверки своего выделенного соединения перед получением advisory lock.
    /// Метод не освобождает переданное соединение и не выполняет миграции.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Проверка отменена.</exception>
    /// <exception cref="NpgsqlException">База данных недоступна или не удалось выполнить проверочный запрос.</exception>
    /// <example>
    /// <code>
    /// await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
    /// await Database.CheckConnectionAsync(connection, cancellationToken);
    /// </code>
    /// </example>
    public static async Task CheckConnectionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
    }
}
