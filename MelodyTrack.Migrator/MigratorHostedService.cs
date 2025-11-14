using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Migrator.OldData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace MelodyTrack.Migrator;

public class MigratorHostedService(AppV1DbContext oldDb, AppDbContext db) : IHostedService
{

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var oldClients = await oldDb.Clients
            .Include(oldClient => oldClient.Contacts)
            .ToListAsync(cancellationToken);

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

        await db.Clients.AddRangeAsync(clients, cancellationToken);

        var oldExpenses = await oldDb.Expenses
            .ToListAsync(cancellationToken);

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

        await db.Expenses.AddRangeAsync(expenses, cancellationToken);
        
        var oldServices = await oldDb.Services
            .ToListAsync(cancellationToken);

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

        await db.Services.AddRangeAsync(services, cancellationToken);
        
        var oldPayments = await oldDb.Payments
            .Include(oldPayment => oldPayment.Service)
            .Include(e => e.Client)
            .ToListAsync(cancellationToken);

        List<Payment> payments = [];

        foreach (var payment in oldPayments)
        {

            var client = await db.Clients
                .AsNoTracking()
                .Where(e => e.FirstName == payment.Client.FirstName && e.LastName == payment.Client.LastName)
                .Include(e => e.Contacts)
                .FirstOrDefaultAsync(cancellationToken);

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
                .FirstOrDefaultAsync(cancellationToken);

            if (service is null && payment.Service is not null)
            {
                service = new Service
                {
                    Id = Ulid.NewUlid(),
                    Name = payment.Service.Name,
                    Description = payment.Service.Description,
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

        await db.Payments.AddRangeAsync(payments, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}