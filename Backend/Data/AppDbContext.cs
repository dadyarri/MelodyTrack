using Backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
///     Контекст базы данных
/// </summary>
/// <param name="options">Параметры</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>
    ///     Клиенты
    /// </summary>
    public virtual DbSet<Client> Clients { get; set; }

    /// <summary>
    ///     Контакты клиентов
    /// </summary>
    public virtual DbSet<ClientContact> ClientContacts { get; set; }

    /// <summary>
    ///     Расходы
    /// </summary>
    public virtual DbSet<Expense> Expenses { get; set; }

    /// <summary>
    ///     Платежи
    /// </summary>
    public virtual DbSet<Payment> Payments { get; set; }

    /// <summary>
    ///     Услуги
    /// </summary>
    public virtual DbSet<Service> Services { get; set; }

    /// <summary>
    ///     Расписание
    /// </summary>
    public virtual DbSet<ServiceHistory> Schedule { get; set; }

    /// <summary>
    ///     История цен на услуги
    /// </summary>
    public virtual DbSet<ServicePriceHistory> ServicePriceHistories { get; set; }

    /// <summary>
    ///     Пользователи
    /// </summary>
    public virtual DbSet<User> Users { get; set; }
}