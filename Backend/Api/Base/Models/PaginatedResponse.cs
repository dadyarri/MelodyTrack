namespace Backend.Api.Base.Models;

/// <summary>
///     Ответ на успешный запрос данных, разбитый на страницы
/// </summary>
/// <typeparam name="T">Тип данных</typeparam>
public class PaginatedResponse<T>
{
    /// <summary>
    ///     Данные
    /// </summary>
    public required List<T> Data { get; set; }

    /// <summary>
    ///     Информация о разбивании на страницы
    /// </summary>
    public required PagedInfo Info { get; set; }

    /// <summary>
    ///     Создать ответ из данных, разделённый на страницы
    /// </summary>
    /// <param name="data">Данные</param>
    /// <param name="count">Количество</param>
    /// <param name="page">Страница</param>
    /// <param name="pageSize">Размер страницы</param>
    /// <param name="skipped">Пропущено записей</param>
    /// <returns>Данные, разделённые на страницы</returns>
    public static PaginatedResponse<T> Create(List<T> data, long count, int page, int pageSize, long skipped)
    {
        return new PaginatedResponse<T>
        {
            Data = data,
            Info = new PagedInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = count,
                HasNextPage = skipped + pageSize < count,
                HasPrevPage = page > 1
            }
        };
    }
}

/// <summary>
///     Информация о данных, разделённых на страницы
/// </summary>
public class PagedInfo
{
    /// <summary>
    ///     Номер страницы
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    ///     Размер страницы
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    ///     Всего
    /// </summary>
    public long Total { get; set; }

    /// <summary>
    ///     Наличие предыдущей страницы
    /// </summary>
    public bool HasPrevPage { get; set; }

    /// <summary>
    ///     Наличие предыдущей страницы
    /// </summary>
    public bool HasNextPage { get; set; }
}