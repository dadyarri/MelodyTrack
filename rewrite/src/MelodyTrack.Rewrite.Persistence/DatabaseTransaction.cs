using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

/// <summary>
/// Область fluent-запросов, владеющая одним соединением и одной транзакцией PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Создаётся через <see cref="Database.BeginTransactionAsync"/> и используется с <c>await using</c>.
/// Запросы создаются методом <see cref="Query"/> и выполняются последовательно на общем соединении.
/// Разные транзакционные области независимы; общей текущей транзакции в <see cref="Database"/> нет.
/// </para>
/// <para>
/// Фиксация выполняется только явным <see cref="CommitAsync"/>. Освобождение незавершённой области
/// откатывает изменения, в том числе при исключении, отмене запроса или раннем возврате ошибки Result.
/// <see cref="RollbackAsync"/> позволяет откатить изменения явно.
/// </para>
/// <para>
/// После commit или rollback нельзя создавать запросы через <see cref="Query"/> и выполнять уже
/// созданные запросы. Само соединение остаётся во владении области до <see cref="DisposeAsync"/>.
/// Область не предназначена для параллельного выполнения запросов или одновременного освобождения.
/// </para>
/// </remarks>
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
public sealed class DatabaseTransaction : IAsyncDisposable
{
    private readonly Database database;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private bool completed;
    private bool disposed;

    internal DatabaseTransaction(Database database, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.database = database;
        this.connection = connection;
        this.transaction = transaction;
    }

    /// <summary>
    /// Создаёт fluent-запрос, связанный с соединением и транзакцией этой области.
    /// </summary>
    /// <param name="sql">SQL с именованными параметрами, например <c>WHERE id = @id</c>.</param>
    /// <returns>Builder, сохраняющий привязку к области при добавлении параметров.</returns>
    /// <remarks>
    /// Метод не выполняет SQL и не открывает новое соединение. Доступны те же <c>ExecuteAsync</c>
    /// и <c>Get*Async</c>, что и у обычного запроса. Выполнение дополнительно проверяет состояние
    /// области, поэтому запрос нельзя использовать после commit, rollback или освобождения.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">SQL пуст или содержит только пробелы.</exception>
    /// <exception cref="InvalidOperationException">Транзакция уже завершена.</exception>
    /// <exception cref="ObjectDisposedException">Область уже освобождена.</exception>
    /// <example>
    /// <code>
    /// var update = transaction.Query(updateSql).WithParameter("id", clientId.Value);
    /// await update.WithParameter("notes", notes).ExecuteAsync(cancellationToken);
    /// </code>
    /// </example>
    public DatabaseQuery Query(string sql)
    {
        EnsureActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new DatabaseQuery(database, sql, this);
    }

    /// <summary>
    /// Фиксирует изменения транзакции и запрещает дальнейшее выполнение связанных запросов.
    /// </summary>
    /// <param name="cancellationToken">Отмена операции фиксации.</param>
    /// <returns>Асинхронная операция commit.</returns>
    /// <remarks>
    /// Вызывайте только после успешного завершения всех проверок и запросов прикладной операции.
    /// Успешный commit завершает транзакцию, но соединение освобождается при выходе из <c>await using</c>.
    /// Повторный commit и последующий rollback не допускаются.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Транзакция уже завершена.</exception>
    /// <exception cref="ObjectDisposedException">Область уже освобождена.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена.</exception>
    /// <exception cref="NpgsqlException">Не удалось выполнить commit.</exception>
    /// <example>
    /// <code>
    /// await transaction.Query(updateSql).WithParameter("id", id).ExecuteAsync(cancellationToken);
    /// await transaction.CommitAsync(cancellationToken);
    /// </code>
    /// </example>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        completed = true;
    }

    /// <summary>
    /// Явно откатывает изменения и завершает транзакцию.
    /// </summary>
    /// <param name="cancellationToken">Отмена явного отката.</param>
    /// <returns>Асинхронная операция rollback.</returns>
    /// <remarks>
    /// После успешного отката область больше не принимает запросы, а соединение освобождается
    /// при <see cref="DisposeAsync"/>. Для простого выхода без commit явный вызов необязателен:
    /// незавершённую транзакцию откатит <c>await using</c>. Этот автоматический откат не использует
    /// токен запроса и выполняется также после его отмены.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Транзакция уже завершена.</exception>
    /// <exception cref="ObjectDisposedException">Область уже освобождена.</exception>
    /// <exception cref="OperationCanceledException">Явный откат отменён.</exception>
    /// <exception cref="NpgsqlException">Не удалось выполнить rollback.</exception>
    /// <example>
    /// <code>
    /// if (hasConflict)
    /// {
    ///     await transaction.RollbackAsync(cancellationToken);
    ///     return;
    /// }
    /// </code>
    /// </example>
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.RollbackAsync(cancellationToken);
        completed = true;
    }

    internal Task<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return operation(connection, transaction, cancellationToken);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException("Транзакция уже завершена.");
        }
    }

    /// <summary>
    /// Откатывает незавершённую транзакцию и освобождает её соединение.
    /// </summary>
    /// <returns>Асинхронная операция освобождения ресурсов.</returns>
    /// <remarks>
    /// <para>
    /// Обычно вызывается автоматически конструкцией <c>await using</c>. Метод никогда не выполняет commit,
    /// не зависит от токена отмены запроса и безопасен при повторном последовательном вызове.
    /// </para>
    /// <para>
    /// Освобождение соединения вызывается также при ошибке освобождения транзакции.
    /// Ошибки провайдера при очистке не подавляются. После вызова нельзя создавать или выполнять
    /// связанные запросы, а также вызывать commit или rollback.
    /// </para>
    /// </remarks>
    /// <exception cref="NpgsqlException">Ошибка отката или освобождения ресурсов провайдера.</exception>
    /// <example>
    /// <code>
    /// await using (var transaction = await database.BeginTransactionAsync(cancellationToken))
    /// {
    ///     await transaction.Query(updateSql).WithParameter("id", id).ExecuteAsync(cancellationToken);
    ///     // Commit не вызывается: при выходе изменения будут отменены.
    /// }
    /// </code>
    /// </example>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
