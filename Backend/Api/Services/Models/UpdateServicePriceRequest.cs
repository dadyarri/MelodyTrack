namespace Backend.Api.Services.Models;

/// <summary>
/// Запрос на обновление цены
/// </summary>
public class UpdateServicePriceRequest
{
    /// <summary>
    /// Цена
    /// </summary>
    public required decimal Price { get; set; }
}