using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MelodyTrack.Data.Initialization;

public sealed class DevelopmentDemoDataSeeder(
    AppDbContext db,
    IPersonalDataProtector personalDataProtector,
    TimeProvider timeProvider,
    ILogger<DevelopmentDemoDataSeeder> logger)
{
    private const string DevelopmentEmail = "dev.superuser@melodytrack.local";
    private const string DemoTag = "[demo-v3]";
    private const string UpcomingDemoTag = "[demo-v5]";
    private const string DevelopmentTimeZoneId = "Europe/Moscow";

    private static readonly string[] ClientFirstNames =
    [
        "Анна", "Мария", "София", "Алиса", "Полина", "Екатерина",
        "Дарья", "Виктория", "Елена", "Александр", "Михаил", "Иван",
        "Артём", "Максим", "Даниил", "Никита", "Кирилл", "Роман"
    ];

    private static readonly string[] ClientLastNames =
    [
        "Соколова", "Морозова", "Волкова", "Лебедева", "Новикова", "Фёдорова",
        "Кузнецова", "Попова", "Орлова", "Петров", "Смирнов", "Козлов",
        "Васильев", "Зайцев", "Павлов", "Семёнов", "Голубев", "Виноградов"
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var firstDate = DateOnly.FromDateTime(nowUtc.AddMonths(-6));
        var lastDate = DateOnly.FromDateTime(nowUtc);
        var provider = await ResolveProviderAsync(cancellationToken);
        var sources = await ResolveSourcesAsync(cancellationToken);
        var services = await ResolveServicesAsync(firstDate, cancellationToken);
        var categories = await ResolveExpenseCategoriesAsync(cancellationToken);
        var clients = await CreateClientsAsync(sources, firstDate, cancellationToken);

        var appointments = CreateAppointments(provider, clients, services, firstDate, nowUtc);
        var payments = CreatePayments(appointments, services);
        var expenses = CreateExpenses(categories, firstDate, lastDate);

        await db.Appointments.AddRangeAsync(appointments, cancellationToken);
        await db.Payments.AddRangeAsync(payments, cancellationToken);
        await db.Expenses.AddRangeAsync(expenses, cancellationToken);

        logger.LogInformation(
            "Prepared rolling Development demo data from {FirstDate} through {LastDate}: {ClientCount} clients, {AppointmentCount} appointments, {PaymentCount} payments, and {ExpenseCount} expenses",
            firstDate,
            lastDate,
            clients.Count,
            appointments.Count,
            payments.Count,
            expenses.Count);
    }

    public async Task EnsureProviderAssignmentsAsync(CancellationToken cancellationToken)
    {
        var provider = await ResolveProviderAsync(cancellationToken);
        var appointments = await db.Appointments
            .Where(appointment => appointment.LessonNotes != null && appointment.LessonNotes.StartsWith(DemoTag))
            .ToListAsync(cancellationToken);

        foreach (var appointment in appointments)
        {
            appointment.Provider = provider;
        }

        logger.LogInformation(
            "Assigned {AppointmentCount} Development demo appointments to the deterministic provider",
            appointments.Count);
    }

    public async Task SeedUpcomingAppointmentsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(DevelopmentTimeZoneId);
        var firstLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));
        var provider = await ResolveProviderAsync(cancellationToken);
        var clientIds = Enumerable.Range(0, ClientFirstNames.Length)
            .Select(index => DeterministicId("client", index))
            .ToArray();
        var clients = await db.Clients
            .Where(client => clientIds.Contains(client.Id) && !client.IsLeadClosed)
            .OrderBy(client => client.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        string[] serviceNames = ["Вокал", "Фортепиано", "Гитара", "Сольфеджио", "Знакомство"];
        var services = await db.Services
            .Where(service => serviceNames.Contains(service.Name))
            .OrderBy(service => service.Name)
            .ToListAsync(cancellationToken);

        if (clients.Count == 0 || services.Count == 0)
        {
            throw new InvalidOperationException("Development clients and services must exist before upcoming appointments are seeded.");
        }

        var appointments = new List<Appointment>();
        for (var dayOffset = 0; dayOffset < 21; dayOffset++)
        {
            var date = firstLocalDate.AddDays(dayOffset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var slot = 0; slot < 2; slot++)
            {
                var start = ConvertLocalToUtc(date, 10 + slot * 4, timeZone);
                if (start <= nowUtc)
                {
                    continue;
                }

                var ordinal = dayOffset * 2 + slot;
                appointments.Add(new Appointment
                {
                    Id = DeterministicId("upcoming-appointment", ordinal),
                    Client = clients[ordinal % clients.Count],
                    Service = services[ordinal % services.Count],
                    Provider = provider,
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    Status = AppointmentStatus.Planned,
                    IsDeleted = false,
                    LessonNotes = UpcomingDemoTag
                });
            }
        }

        await db.Appointments.AddRangeAsync(appointments, cancellationToken);
        logger.LogInformation(
            "Prepared {AppointmentCount} upcoming Development demo appointments through {LastLocalDate} in {TimeZoneId}",
            appointments.Count,
            firstLocalDate.AddDays(20),
            DevelopmentTimeZoneId);
    }

    private async Task<User> ResolveProviderAsync(CancellationToken cancellationToken)
    {
        var emailBlindIndex = personalDataProtector.HashEmailBlindIndex(DevelopmentEmail);
        return await db.Users.SingleAsync(
            user => user.EmailBlindIndex == emailBlindIndex,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ClientSource>> ResolveSourcesAsync(CancellationToken cancellationToken)
    {
        string[] names = ["Рекомендации", "ВКонтакте", "Яндекс Карты", "Сайт", "Вывеска"];
        var existing = await db.ClientSources
            .Where(source => names.Contains(source.Name))
            .ToDictionaryAsync(source => source.Name, StringComparer.Ordinal, cancellationToken);
        var result = new List<ClientSource>(names.Length);

        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            if (!existing.TryGetValue(name, out var source))
            {
                source = new ClientSource
                {
                    Id = DeterministicId("source", index),
                    Name = name
                };
                await db.ClientSources.AddAsync(source, cancellationToken);
            }

            result.Add(source);
        }

        return result;
    }

    private async Task<IReadOnlyList<DemoService>> ResolveServicesAsync(
        DateOnly firstDate,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new ServiceDefinition("Вокал", "Вокал", "Индивидуальное занятие по вокалу", 2_200m),
            new ServiceDefinition("Фортепиано", "Фортепиано", "Индивидуальное занятие по фортепиано", 2_400m),
            new ServiceDefinition("Гитара", "Гитара", "Индивидуальное занятие по гитаре", 2_100m),
            new ServiceDefinition("Сольфеджио", "Сольфеджио", "Занятие по музыкальной теории", 1_800m),
            new ServiceDefinition("Знакомство", "Пробное занятие", "Первое знакомство с преподавателем", 1_000m, true)
        };
        var names = definitions.Select(definition => definition.Name).ToArray();
        var existing = await db.Services
            .Where(service => names.Contains(service.Name))
            .ToDictionaryAsync(service => service.Name, StringComparer.Ordinal, cancellationToken);
        var priceIds = definitions
            .SelectMany((_, index) => new[]
            {
                DeterministicId("service-price-initial", index),
                DeterministicId("service-price-current", index)
            })
            .ToArray();
        var existingPriceIds = (await db.ServicePriceHistory
                .Where(price => priceIds.Contains(price.Id))
                .Select(price => price.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var result = new List<DemoService>(definitions.Length);

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            if (!existing.TryGetValue(definition.Name, out var service))
            {
                service = new Service
                {
                    Id = DeterministicId("service", index),
                    Name = definition.Name,
                    PublicName = definition.PublicName,
                    Description = definition.Description,
                    IsConsultation = definition.IsConsultation
                };
                await db.Services.AddAsync(service, cancellationToken);
            }

            var initialPriceId = DeterministicId("service-price-initial", index);
            if (existingPriceIds.Add(initialPriceId))
            {
                await db.ServicePriceHistory.AddAsync(new ServicePrice
                {
                    Id = initialPriceId,
                    Service = service,
                    EffectiveDate = AtUtc(firstDate, 0),
                    Price = definition.Price
                }, cancellationToken);
            }

            var currentPrice = definition.Price + 200m;
            var currentPriceId = DeterministicId("service-price-current", index);
            if (existingPriceIds.Add(currentPriceId))
            {
                await db.ServicePriceHistory.AddAsync(new ServicePrice
                {
                    Id = currentPriceId,
                    Service = service,
                    EffectiveDate = AtUtc(firstDate.AddMonths(3), 0),
                    Price = currentPrice
                }, cancellationToken);
            }

            result.Add(new DemoService(service, definition.Price, currentPrice));
        }

        return result;
    }

    private async Task<IReadOnlyList<ExpenseCategory>> ResolveExpenseCategoriesAsync(CancellationToken cancellationToken)
    {
        string[] names = ["Аренда", "Реклама", "Оборудование", "Расходники"];
        var existing = await db.ExpenseCategories
            .Where(category => names.Contains(category.Name))
            .ToDictionaryAsync(category => category.Name, StringComparer.Ordinal, cancellationToken);
        var result = new List<ExpenseCategory>(names.Length);

        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            if (!existing.TryGetValue(name, out var category))
            {
                category = new ExpenseCategory
                {
                    Id = DeterministicId("expense-category", index),
                    Name = name
                };
                await db.ExpenseCategories.AddAsync(category, cancellationToken);
            }

            result.Add(category);
        }

        return result;
    }

    private async Task<IReadOnlyList<Client>> CreateClientsAsync(
        IReadOnlyList<ClientSource> sources,
        DateOnly firstDate,
        CancellationToken cancellationToken)
    {
        var clientIds = Enumerable.Range(0, ClientFirstNames.Length)
            .Select(index => DeterministicId("client", index))
            .ToArray();
        var existing = await db.Clients
            .Where(client => clientIds.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, cancellationToken);
        var result = new List<Client>(ClientFirstNames.Length);

        for (var index = 0; index < ClientFirstNames.Length; index++)
        {
            var clientId = DeterministicId("client", index);
            if (!existing.TryGetValue(clientId, out var client))
            {
                client = new Client
                {
                    Id = clientId,
                    FirstName = ClientFirstNames[index],
                    LastName = ClientLastNames[index],
                    DateOfBirth = new DateOnly(1988 + index % 20, index % 12 + 1, index % 25 + 1),
                    Source = sources[index % sources.Count],
                    CreatedAtUtc = AtUtc(firstDate.AddDays(index * 5), 9 + index % 8),
                    IsLeadClosed = index is 4 or 13,
                    Contacts = new ClientContacts
                    {
                        Id = DeterministicId("client-contacts", index),
                        Email = $"client{index + 1:00}@demo.melodytrack.local",
                        Phone = $"+7999000{index + 1:0000}"
                    }
                };
                await db.Clients.AddAsync(client, cancellationToken);
            }

            result.Add(client);
        }

        return result;
    }

    private static List<Appointment> CreateAppointments(
        User provider,
        IReadOnlyList<Client> clients,
        IReadOnlyList<DemoService> services,
        DateOnly firstDate,
        DateTime nowUtc)
    {
        var result = new List<Appointment>();
        var ordinal = 0;

        for (var week = 0; week < 27; week++)
        {
            for (var slot = 0; slot < 10; slot++)
            {
                var date = firstDate.AddDays(week * 7 + slot % 5);
                var start = AtUtc(date, 10 + slot / 5 * 4);
                if (start >= nowUtc)
                {
                    continue;
                }

                var eligibleClients = clients
                    .Where(client => !client.IsLeadClosed && client.CreatedAtUtc <= start)
                    .ToArray();
                var status = ordinal % 17 == 0
                    ? AppointmentStatus.Cancelled
                    : ordinal % 13 == 0
                        ? AppointmentStatus.Burned
                        : AppointmentStatus.Completed;
                result.Add(new Appointment
                {
                    Id = DeterministicId("appointment", ordinal),
                    Client = eligibleClients[(week * 7 + slot) % eligibleClients.Length],
                    Service = services[(week + slot) % services.Count].Entity,
                    Provider = provider,
                    StartDate = start,
                    EndDate = start.AddHours(1),
                    Status = status,
                    IsDeleted = false,
                    LessonNotes = ordinal % 4 == 0 ? $"{DemoTag} Отработан учебный материал" : DemoTag
                });
                ordinal++;
            }
        }

        return result;
    }

    private static List<Payment> CreatePayments(
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<DemoService> services)
    {
        var result = new List<Payment>();
        var firstAppointmentDate = appointments.Min(appointment => appointment.StartDate).Date;
        var midpoint = firstAppointmentDate.AddMonths(3);

        for (var index = 0; index < appointments.Count; index++)
        {
            var appointment = appointments[index];
            if (appointment.Status == AppointmentStatus.Cancelled || index % 9 == 0)
            {
                continue;
            }

            var service = services.Single(item => item.Entity == appointment.Service);
            var candidateDate = appointment.StartDate.AddHours(-(index % 3) * 12);
            result.Add(new Payment
            {
                Id = DeterministicId("payment", index),
                Client = appointment.Client,
                Service = appointment.Service,
                Amount = appointment.StartDate < midpoint ? service.InitialPrice : service.CurrentPrice,
                Date = candidateDate < firstAppointmentDate ? firstAppointmentDate : candidateDate,
                Description = $"{DemoTag} Оплата демонстрационного занятия"
            });
        }

        return result;
    }

    private static List<Expense> CreateExpenses(
        IReadOnlyList<ExpenseCategory> categories,
        DateOnly firstDate,
        DateOnly lastDate)
    {
        var result = new List<Expense>();
        var month = new DateOnly(firstDate.Year, firstDate.Month, 1);
        var ordinal = 0;

        while (month <= lastDate)
        {
            for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                var date = month.AddDays(2 + categoryIndex * 4);
                if (date < firstDate || date > lastDate)
                {
                    continue;
                }

                result.Add(new Expense
                {
                    Id = DeterministicId("expense", ordinal),
                    Category = categories[categoryIndex],
                    Amount = categoryIndex switch
                    {
                        0 => 35_000m + ordinal % 3 * 1_000m,
                        1 => 8_000m + ordinal % 4 * 500m,
                        2 => 12_000m + ordinal % 5 * 1_500m,
                        _ => 3_000m + ordinal % 6 * 300m
                    },
                    Date = AtUtc(date, 12),
                    Description = $"{DemoTag} Демонстрационные расходы: {categories[categoryIndex].Name.ToLowerInvariant()}"
                });
                ordinal++;
            }

            month = month.AddMonths(1);
        }

        return result;
    }

    private static DateTime AtUtc(DateOnly date, int hour)
    {
        return DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);
    }

    private static DateTime ConvertLocalToUtc(DateOnly date, int hour, TimeZoneInfo timeZone)
    {
        var localDateTime = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
    }

    private static Ulid DeterministicId(string entity, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"melodytrack-development-v3:{entity}:{index}"));
        return new Ulid(hash[..16]);
    }

    private sealed record ServiceDefinition(
        string Name,
        string PublicName,
        string Description,
        decimal Price,
        bool IsConsultation = false);

    private sealed record DemoService(Service Entity, decimal InitialPrice, decimal CurrentPrice);
}
