using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.LegacyDataMigrator.OldData;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.MigratorService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migrating database...");

        using var scope = _scopeFactory.CreateScope();
        var newDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quartzScript = await File.ReadAllTextAsync("quartz.sql", stoppingToken);

        await newDb.Database.MigrateAsync(stoppingToken);
        await newDb.Database.ExecuteSqlRawAsync(quartzScript, stoppingToken);

        await MigrateV1ToV2(stoppingToken);

        _hostApplicationLifetime.StopApplication();
    }

    private async Task MigrateV1ToV2(CancellationToken stoppingToken)
    {

        _logger.LogInformation("Migrating data from old database to new...");
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldDb = scope.ServiceProvider.GetService<AppV1DbContext>();

        if (oldDb is null)
        {
            _logger.LogWarning("Old database connection string was not set, exiting.");
            return;
        }

        var oldClients = await oldDb.Clients
            .Include(oldClient => oldClient.Contacts)
            .ToListAsync(stoppingToken);

        List<Client> clients = [];

        foreach (var client in oldClients)
        {
            var contacts = new ClientContacts
            {
                Id = Ulid.NewUlid(),
                Phone = client.Contacts?.Phone,
                Telegram = client.Contacts?.Telegram,
                Vk = client.Contacts?.Vk
            };
            clients.Add(
                new Client
                {
                    Id = Ulid.NewUlid(),
                    FirstName = client.FirstName,
                    LastName = client.LastName,
                    Patronymic = client.Patronymic,
                    Contacts = contacts
                }
            );
        }

        _logger.LogInformation("Copied {AmountOfClients} clients to new database", clients.Count);

        await db.Clients.AddRangeAsync(clients, stoppingToken);

        var oldExpenses = await oldDb.Expenses
            .ToListAsync(stoppingToken);

        List<Expense> expenses = [];

        foreach (var expense in oldExpenses)
        {
            expenses.Add(new Expense
            {
                Id = Ulid.NewUlid(),
                Amount = expense.Amount,
                Date = expense.Date,
                Description = expense.Description
            });
        }

        await db.Expenses.AddRangeAsync(expenses, stoppingToken);

        var oldServices = await oldDb.Services
            .ToListAsync(stoppingToken);

        List<Service> services = [];

        foreach (var service in oldServices)
        {
            services.Add(new Service
            {
                Id = Ulid.NewUlid(),
                Name = service.Name,
                Description = service.Description
            });
        }

        await db.Services.AddRangeAsync(services, stoppingToken);

        var oldPayments = await oldDb.Payments
            .Include(oldPayment => oldPayment.Service)
            .Include(e => e.Client)
            .ToListAsync(stoppingToken);

        List<Payment> payments = [];

        foreach (var payment in oldPayments)
        {

            var client = await db.Clients
                .AsNoTracking()
                .Where(e => e.FirstName == payment.Client.FirstName && e.LastName == payment.Client.LastName)
                .Include(e => e.Contacts)
                .FirstOrDefaultAsync(stoppingToken);

            if (client is null)
            {
                client = new Client
                {
                    Id = Ulid.NewUlid(),
                    FirstName = payment.Client.FirstName,
                    LastName = payment.Client.LastName,
                    Patronymic = payment.Client.Patronymic,
                    Contacts = new ClientContacts
                    {
                        Id = Ulid.NewUlid(),
                        Phone = payment.Client.Contacts?.Phone,
                        Telegram = payment.Client.Contacts?.Telegram,
                        Vk = payment.Client.Contacts?.Vk
                    }
                };
            }

            var service = await db.Services
                .AsNoTracking()
                .Where(e => payment.Service == null || e.Name == payment.Service.Name)
                .FirstOrDefaultAsync(stoppingToken);

            if (service is null && payment.Service is not null)
            {
                service = new Service
                {
                    Id = Ulid.NewUlid(),
                    Name = payment.Service.Name,
                    Description = payment.Service.Description
                };
            }
            else if (service is null && payment.Service is null)
            {
                service = null;
            }

            payments.Add(new Payment
            {
                Id = Ulid.NewUlid(),
                Amount = payment.Amount,
                Date = payment.Date,
                Description = payment.Description,
                Client = client,
                Service = service
            });
        }

        await db.Payments.AddRangeAsync(payments, stoppingToken);

        var oldServiceHistory = await oldDb.Schedule
            .Include(oldService => oldService.Service)
            .Include(e => e.Client)
            .ToListAsync(stoppingToken);

        List<Appointment> appointments = [];

        foreach (var serviceHistory in oldServiceHistory)
        {
            var client = await db.Clients
                .AsNoTracking()
                .Where(e => e.FirstName == serviceHistory.Client.FirstName && e.LastName == serviceHistory.Client.LastName)
                .Include(e => e.Contacts)
                .FirstOrDefaultAsync(stoppingToken);

            if (client is null)
            {
                client = new Client
                {
                    Id = Ulid.NewUlid(),
                    FirstName = serviceHistory.Client.FirstName,
                    LastName = serviceHistory.Client.LastName,
                    Patronymic = serviceHistory.Client.Patronymic,
                    Contacts = new ClientContacts
                    {
                        Id = Ulid.NewUlid(),
                        Phone = serviceHistory.Client.Contacts?.Phone,
                        Telegram = serviceHistory.Client.Contacts?.Telegram,
                        Vk = serviceHistory.Client.Contacts?.Vk
                    }
                };
            }

            var service = await db.Services
                .AsNoTracking()
                .Where(e => e.Name == serviceHistory.Service.Name)
                .FirstOrDefaultAsync(stoppingToken);

            if (service is null)
            {
                service = new Service
                {
                    Id = Ulid.NewUlid(),
                    Name = serviceHistory.Service.Name,
                    Description = serviceHistory.Service.Description
                };
            }
            appointments.Add(new Appointment
            {
                Id = Ulid.NewUlid(),
                StartDate = serviceHistory.StartDate,
                EndDate = serviceHistory.EndDate,
                IsCompleted = serviceHistory.Completed,
                IsCanceled = false,
                Provider = null,
                RecurringRule = null,
                Client = client,
                Service = service
            });
        }

        await db.Appointments.AddRangeAsync(appointments, stoppingToken);

        await db.SaveChangesAsync(stoppingToken);
    }
}