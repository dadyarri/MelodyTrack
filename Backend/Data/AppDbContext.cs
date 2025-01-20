using Backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public class AppDbContext : DbContext
{
    public virtual DbSet<Client> Clients { get; set; }
    public virtual DbSet<ClientContact> ClientContacts { get; set; }
    public virtual DbSet<Expense> Expenses { get; set; }
    public virtual DbSet<Payment> Payments { get; set; }
    public virtual DbSet<Service> Services { get; set; }
    public virtual DbSet<ServiceHistory> Schedule { get; set; }
    public virtual DbSet<ServicePriceHistory> ServicePriceHistories { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
    {
    }
}