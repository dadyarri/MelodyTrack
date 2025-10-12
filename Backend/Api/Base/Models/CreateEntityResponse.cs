using JetBrains.Annotations;

namespace Backend.Api.Base.Models;

/// <summary>
///     Ответ на успешный запрос создания сущности
/// </summary>
[UsedImplicitly]
public class CreateEntityResponse
{
    /// <summary>
    ///     Идентификатор сущности
    /// </summary>
    public long Id { get; set; }
}