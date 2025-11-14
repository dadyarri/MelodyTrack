using MelodyTrack.Migrator.OldData.Entities;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Migrator.OldData;

/// <summary>
///     Контекст базы данных
/// </summary>
/// <param name="options">Параметры</param>
public class AppV1DbContext(DbContextOptions<AppV1DbContext> options) : DbContext(options)
{
    /// <summary>
    ///     Клиенты
    /// </summary>
    public virtual DbSet<OldClient> Clients { get; set; }

    /// <summary>
    ///     Контакты клиентов
    /// </summary>
    public virtual DbSet<OldClientContact> ClientContacts { get; set; }

    /// <summary>
    ///     Расходы
    /// </summary>
    public virtual DbSet<OldExpense> Expenses { get; set; }

    /// <summary>
    ///     Платежи
    /// </summary>
    public virtual DbSet<OldPayment> Payments { get; set; }

    /// <summary>
    ///     Услуги
    /// </summary>
    public virtual DbSet<OldService> Services { get; set; }

    /// <summary>
    ///     Расписание
    /// </summary>
    public virtual DbSet<OldServiceHistory> Schedule { get; set; }

    /// <summary>
    ///     История цен на услуги
    /// </summary>
    public virtual DbSet<OldServicePriceHistory> ServicePriceHistories { get; set; }

    /// <summary>
    ///     Пользователи
    /// </summary>
    public virtual DbSet<OldUser> Users { get; set; }
}