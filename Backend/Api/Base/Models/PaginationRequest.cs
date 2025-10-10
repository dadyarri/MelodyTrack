using FastEndpoints;
using JetBrains.Annotations;

namespace Backend.Api.Base.Models;

/// <summary>
/// Запрос данных постранично
/// </summary>
[UsedImplicitly]
public class PaginationRequest
{
    /// <summary>
    /// Номер страницы
    /// </summary>
    [BindFrom("page")]
    public int Page { get; set; }

    /// <summary>
    /// Размер страницы
    /// </summary>
    [BindFrom("page_size")]
    public int PageSize { get; set; }
}