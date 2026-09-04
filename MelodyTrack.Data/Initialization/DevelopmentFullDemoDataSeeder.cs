using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MelodyTrack.Data.Initialization;

public sealed class DevelopmentFullDemoDataSeeder(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<DevelopmentFullDemoDataSeeder> logger)
{
    private const int ClientCount = 48;
    private const string DevelopmentTimeZoneId = "Europe/Moscow";
    private static readonly Ulid DevelopmentProviderId = Ulid.Parse("01K00000000000000000000001");

    private static readonly string[] FirstNames =
    [
        "Анна", "Мария", "София", "Алиса", "Полина", "Екатерина", "Дарья", "Виктория",
        "Елена", "Александр", "Михаил", "Иван", "Артём", "Максим", "Даниил", "Никита",
        "Кирилл", "Роман", "Ольга", "Наталья", "Ирина", "Татьяна", "Юлия", "Ксения"
    ];

    private static readonly string[] LastNames =
    [
        "Соколова", "Морозова", "Волкова", "Лебедева", "Новикова", "Фёдорова", "Кузнецова", "Попова",
        "Орлова", "Петров", "Смирнов", "Козлов", "Васильев", "Зайцев", "Павлов", "Семёнов",
        "Голубев", "Виноградов", "Беляева", "Тарасова", "Комарова", "Богданова", "Воронова", "Филиппова"
    ];

    private static readonly string[] LessonNotes =
    [
        "Повторить упражнение на дыхание.",
        "Хорошо разобрали домашнее задание.",
        "На следующем занятии продолжить с середины произведения.",
        "Обратить внимание на ритм.",
        "Закрепить материал дома."
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(DevelopmentTimeZoneId);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));
        var firstLocalDate = localToday.AddMonths(-6);
        var scheduleEndLocalDate = EarlierDate(
            localToday.AddDays(21),
            new DateOnly(localToday.Year, 12, 31));
        var random = new Random(firstLocalDate.Year * 100 + firstLocalDate.Month);

        var providers = await ResolveProvidersAsync(cancellationToken);
        var availabilities = providers.ToDictionary(provider => provider.Id, CreateAvailability);
        var sources = await ResolveSourcesAsync(cancellationToken);
        var services = await ResolveServicesAsync(firstLocalDate, localToday, timeZone, cancellationToken);
        var categories = await ResolveExpenseCategoriesAsync(cancellationToken);
        var clients = await ResolveClientsAsync(random, sources, firstLocalDate, localToday, timeZone, cancellationToken);
        var clientIds = clients.Select(client => client.Id).ToArray();
        var existingAppointments = await db.Appointments
            .Include(appointment => appointment.Client)
            .Include(appointment => appointment.Service)
            .Include(appointment => appointment.Provider)
            .Where(appointment => appointment.StartDate >= ConvertLocalToUtc(firstLocalDate, 0, timeZone)
                && appointment.StartDate < ConvertLocalToUtc(scheduleEndLocalDate.AddDays(1), 0, timeZone))
            .ToListAsync(cancellationToken);
        var existingPayments = await db.Payments
            .Include(payment => payment.Client)
            .Include(payment => payment.Service)
            .Where(payment => clientIds.Contains(payment.Client.Id))
            .ToListAsync(cancellationToken);

        var candidateSlots = CreateCandidateSlots(
            providers,
            availabilities,
            firstLocalDate,
            scheduleEndLocalDate,
            timeZone);
        var appointments = CreateAppointments(
            random,
            clients,
            services,
            candidateSlots,
            existingAppointments,
            nowUtc,
            timeZone);
        var payments = CreatePayments(random, appointments, services, nowUtc);
        EnsureDebtLimit(
            existingAppointments.Concat(appointments).ToList(),
            existingPayments,
            payments,
            services,
            nowUtc);
        var expenses = CreateExpenses(random, categories, firstLocalDate, localToday, timeZone);
        var recurrenceRules = await CreateRecurrenceRulesAsync(
            random,
            clients,
            services,
            candidateSlots,
            existingAppointments,
            appointments,
            nowUtc,
            timeZone,
            cancellationToken);

        ValidateSeedData(
            appointments,
            existingAppointments,
            payments,
            existingPayments,
            recurrenceRules,
            services,
            availabilities,
            timeZone,
            nowUtc);

        await db.Appointments.AddRangeAsync(appointments, cancellationToken);
        await db.Payments.AddRangeAsync(payments, cancellationToken);
        await db.Expenses.AddRangeAsync(expenses, cancellationToken);
        await db.RecurrenceRules.AddRangeAsync(recurrenceRules, cancellationToken);

        logger.LogInformation(
            "Prepared full Development demo upgrade: {ClientCount} clients, {AppointmentCount} additional appointments, {PaymentCount} payments, {ExpenseCount} expenses, and {RecurrenceRuleCount} weekly recurrence rules",
            clients.Count,
            appointments.Count,
            payments.Count,
            expenses.Count,
            recurrenceRules.Count);
    }

    private async Task<List<User>> ResolveProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = await db.Users
            .Include(user => user.Role)
            .Include(user => user.WorkingHours)
            .Include(user => user.Vacations)
            .Where(user => user.Role.RoleName != UserRoles.Client)
            .OrderBy(user => user.Id == DevelopmentProviderId ? 0 : 1)
            .ThenBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Take(4)
            .ToListAsync(cancellationToken);

        if (providers.Count == 0)
        {
            throw new InvalidOperationException("At least one non-client Development provider must exist before demo data is seeded.");
        }

        return providers;
    }

    private async Task<List<ClientSource>> ResolveSourcesAsync(CancellationToken cancellationToken)
    {
        string[] names = ["Рекомендации", "ВКонтакте", "Яндекс Карты", "Сайт", "Вывеска"];
        var existing = await db.ClientSources
            .Where(source => names.Contains(source.Name))
            .ToDictionaryAsync(source => source.Name, StringComparer.Ordinal, cancellationToken);
        var result = new List<ClientSource>(names.Length);

        for (var index = 0; index < names.Length; index++)
        {
            if (!existing.TryGetValue(names[index], out var source))
            {
                source = new ClientSource { Id = DeterministicId("source", index), Name = names[index] };
                await db.ClientSources.AddAsync(source, cancellationToken);
            }

            result.Add(source);
        }

        return result;
    }

    private async Task<List<FullDemoService>> ResolveServicesAsync(
        DateOnly firstLocalDate,
        DateOnly localToday,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        ServiceDefinition[] definitions =
        [
            new("Вокал", "Вокал", "Индивидуальное занятие по вокалу", false, 2_200m),
            new("Фортепиано", "Фортепиано", "Индивидуальное занятие по фортепиано", false, 2_400m),
            new("Гитара", "Гитара", "Индивидуальное занятие по гитаре", false, 2_100m),
            new("Сольфеджио", "Сольфеджио", "Занятие по музыкальной теории", false, 1_800m),
            new("Знакомство", "Пробное занятие", "Первое знакомство с преподавателем", true, 1_000m)
        ];
        var names = definitions.Select(definition => definition.Name).ToArray();
        var existingServices = await db.Services
            .Where(service => names.Contains(service.Name))
            .ToDictionaryAsync(service => service.Name, StringComparer.Ordinal, cancellationToken);
        var existingPrices = await db.ServicePriceHistory
            .Include(price => price.Service)
            .Where(price => names.Contains(price.Service.Name))
            .ToListAsync(cancellationToken);
        var priceDates = new[] { firstLocalDate, firstLocalDate.AddMonths(3), localToday };
        var result = new List<FullDemoService>(definitions.Length);

        for (var serviceIndex = 0; serviceIndex < definitions.Length; serviceIndex++)
        {
            var definition = definitions[serviceIndex];
            if (!existingServices.TryGetValue(definition.Name, out var service))
            {
                service = new Service
                {
                    Id = DeterministicId("service", serviceIndex),
                    Name = definition.Name,
                    PublicName = definition.PublicName,
                    Description = definition.Description,
                    IsConsultation = definition.IsConsultation
                };
                await db.Services.AddAsync(service, cancellationToken);
            }

            var prices = existingPrices.Where(price => price.Service.Id == service.Id).ToList();
            for (var priceIndex = 0; priceIndex < priceDates.Length; priceIndex++)
            {
                var effectiveDate = ConvertLocalToUtc(priceDates[priceIndex], 0, timeZone);
                if (prices.Any(price => price.EffectiveDate == effectiveDate))
                {
                    continue;
                }

                var price = new ServicePrice
                {
                    Id = DeterministicId($"service-price-{serviceIndex}", priceIndex),
                    Service = service,
                    EffectiveDate = effectiveDate,
                    Price = definition.BasePrice + priceIndex * 200m
                };
                prices.Add(price);
                await db.ServicePriceHistory.AddAsync(price, cancellationToken);
            }

            result.Add(new FullDemoService(service, prices.OrderBy(price => price.EffectiveDate).ToList()));
        }

        return result;
    }

    private async Task<List<ExpenseCategory>> ResolveExpenseCategoriesAsync(CancellationToken cancellationToken)
    {
        string[] names = ["Аренда", "Реклама", "Оборудование", "Расходники"];
        var existing = await db.ExpenseCategories
            .Where(category => names.Contains(category.Name))
            .ToDictionaryAsync(category => category.Name, StringComparer.Ordinal, cancellationToken);
        var result = new List<ExpenseCategory>(names.Length);

        for (var index = 0; index < names.Length; index++)
        {
            if (!existing.TryGetValue(names[index], out var category))
            {
                category = new ExpenseCategory { Id = DeterministicId("expense-category", index), Name = names[index] };
                await db.ExpenseCategories.AddAsync(category, cancellationToken);
            }

            result.Add(category);
        }

        return result;
    }

    private async Task<List<Client>> ResolveClientsAsync(
        Random random,
        IReadOnlyList<ClientSource> sources,
        DateOnly firstLocalDate,
        DateOnly localToday,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var clientIds = Enumerable.Range(0, ClientCount).Select(index => LegacyClientId(index)).ToArray();
        var existing = await db.Clients
            .Include(client => client.Contacts)
            .Where(client => clientIds.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, cancellationToken);
        var latestCreationDate = EarlierDate(localToday, firstLocalDate.AddMonths(4));
        var creationRange = Math.Max(1, latestCreationDate.DayNumber - firstLocalDate.DayNumber + 1);
        var result = new List<Client>(ClientCount);

        for (var index = 0; index < ClientCount; index++)
        {
            var id = LegacyClientId(index);
            if (!existing.TryGetValue(id, out var client))
            {
                var createdDate = firstLocalDate.AddDays(random.Next(creationRange));
                var firstName = FirstNames[index % FirstNames.Length];
                var lastName = LastNames[(index * 5 + index / FirstNames.Length) % LastNames.Length];
                var age = random.Next(8, 62);
                client = new Client
                {
                    Id = id,
                    FirstName = firstName,
                    LastName = lastName,
                    DateOfBirth = firstLocalDate.AddYears(-age).AddDays(-random.Next(365)),
                    Source = sources[random.Next(sources.Count)],
                    CreatedAtUtc = ConvertLocalToUtc(createdDate, random.Next(9, 19), timeZone)
                        .AddMinutes(random.Next(60)),
                    IsLeadClosed = random.NextDouble() < 0.12,
                    Contacts = new ClientContacts
                    {
                        Id = DeterministicId("client-contacts", index),
                        Email = $"{Transliterate(firstName)}.{Transliterate(lastName)}{index + 1:00}@demo.melodytrack.local",
                        Phone = $"+7900{index + 1:0000000}",
                        Telegram = $"https://t.me/melody_demo_{index + 1:00}",
                        Vk = $"https://vk.com/melody_demo_{index + 1:00}"
                    }
                };
                await db.Clients.AddAsync(client, cancellationToken);
            }
            else
            {
                client.Contacts.Telegram ??= $"https://t.me/melody_demo_{index + 1:00}";
                client.Contacts.Vk ??= $"https://vk.com/melody_demo_{index + 1:00}";
            }

            result.Add(client);
        }

        return result;
    }

    private static List<ProviderSlot> CreateCandidateSlots(
        IReadOnlyList<User> providers,
        IReadOnlyDictionary<Ulid, ProviderAvailability> availabilities,
        DateOnly firstLocalDate,
        DateOnly lastLocalDate,
        TimeZoneInfo timeZone)
    {
        var result = new List<ProviderSlot>();
        foreach (var provider in providers)
        {
            var availability = availabilities[provider.Id];
            for (var date = firstLocalDate; date <= lastLocalDate; date = date.AddDays(1))
            {
                var workingDay = availability.WorkingHours.FirstOrDefault(day => day.DayOfWeek == date.DayOfWeek);
                if (workingDay is null || !workingDay.IsWorkingDay)
                {
                    continue;
                }

                var firstMinute = ((workingDay.StartMinuteOfDay + 59) / 60) * 60;
                for (var minute = firstMinute; minute + 60 <= workingDay.EndMinuteOfDay; minute += 60)
                {
                    var localStart = date.ToDateTime(new TimeOnly(minute / 60, minute % 60), DateTimeKind.Unspecified);
                    if (timeZone.IsInvalidTime(localStart))
                    {
                        continue;
                    }

                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
                    if (availability.Vacations.Any(vacation => startUtc < vacation.EndDate && startUtc.AddHours(1) > vacation.StartDate))
                    {
                        continue;
                    }

                    result.Add(new ProviderSlot(
                        provider,
                        date,
                        minute,
                        startUtc));
                }
            }
        }

        return result;
    }

    private static List<Appointment> CreateAppointments(
        Random random,
        IReadOnlyList<Client> clients,
        IReadOnlyList<FullDemoService> services,
        IReadOnlyList<ProviderSlot> candidateSlots,
        IReadOnlyList<Appointment> existingAppointments,
        DateTime nowUtc,
        TimeZoneInfo timeZone)
    {
        var result = new List<Appointment>();
        var occupied = existingAppointments
            .Where(appointment => !appointment.IsDeleted && appointment.Provider is not null)
            .Select(appointment => new ScheduledInterval(appointment.Provider!.Id, appointment.StartDate, appointment.EndDate))
            .ToList();
        var ordinal = 0;

        foreach (var daySlots in candidateSlots.GroupBy(slot => slot.LocalDate).OrderBy(group => group.Key))
        {
            var existingDailyCount = existingAppointments.Count(appointment =>
                appointment.Provider is not null
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(appointment.StartDate, timeZone)) == daySlots.Key);
            var desiredDailyCount = random.Next(3, 8);
            var dailyCount = Math.Max(0, desiredDailyCount - existingDailyCount);
            var availableSlots = daySlots
                .Where(slot => !Overlaps(occupied, slot.Provider.Id, slot.StartUtc, slot.StartUtc.AddHours(1)))
                .OrderBy(_ => random.Next())
                .Take(dailyCount)
                .ToArray();

            foreach (var slot in availableSlots)
            {
                var eligibleClients = clients
                    .Where(client => !client.IsLeadClosed && client.CreatedAtUtc <= slot.StartUtc)
                    .ToArray();
                if (eligibleClients.Length == 0)
                {
                    continue;
                }

                var status = slot.StartUtc >= nowUtc ? AppointmentStatus.Planned : PastAppointmentStatus(random);
                var appointment = new Appointment
                {
                    Id = DeterministicId("appointment", ordinal),
                    Client = Pick(random, eligibleClients),
                    Service = Pick(random, services).Entity,
                    Provider = slot.Provider,
                    StartDate = slot.StartUtc,
                    EndDate = slot.StartUtc.AddHours(1),
                    Status = status,
                    IsDeleted = slot.StartUtc < nowUtc && random.NextDouble() < 0.012,
                    LessonNotes = status == AppointmentStatus.Completed && random.NextDouble() < 0.32
                        ? Pick(random, LessonNotes)
                        : null
                };
                result.Add(appointment);
                occupied.Add(new ScheduledInterval(slot.Provider.Id, appointment.StartDate, appointment.EndDate));
                ordinal++;
            }
        }

        if (!result.Any(appointment => appointment.IsDeleted))
        {
            var historicalAppointment = result.FirstOrDefault(appointment => appointment.StartDate < nowUtc);
            if (historicalAppointment is not null)
            {
                historicalAppointment.IsDeleted = true;
            }
        }

        return result;
    }

    private static List<Payment> CreatePayments(
        Random random,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<FullDemoService> services,
        DateTime nowUtc)
    {
        var priceHistory = services.ToDictionary(service => service.Entity.Id, service => service.Prices);
        var billableAppointments = appointments
            .Where(appointment => !appointment.IsDeleted
                && appointment.StartDate < nowUtc
                && appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Burned)
            .OrderBy(appointment => appointment.StartDate)
            .ToList();
        var result = new List<Payment>();
        var ordinal = 0;

        foreach (var clientAppointments in billableAppointments.GroupBy(appointment => appointment.Client.Id))
        {
            var ordered = clientAppointments.OrderBy(appointment => appointment.StartDate).ToArray();
            var outstandingCount = random.Next(0, Math.Min(2, ordered.Length) + 1);
            foreach (var appointment in ordered.Take(ordered.Length - outstandingCount))
            {
                var paidAt = appointment.StartDate.AddDays(random.Next(8)).AddHours(random.Next(5));
                result.Add(new Payment
                {
                    Id = DeterministicId("payment", ordinal++),
                    Client = appointment.Client,
                    Service = appointment.Service,
                    Amount = PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate),
                    Date = paidAt <= nowUtc ? paidAt : nowUtc,
                    Description = "Оплата занятия"
                });
            }
        }

        foreach (var client in appointments
                     .Where(appointment => appointment.StartDate >= nowUtc && appointment.StartDate <= nowUtc.AddDays(21))
                     .Select(appointment => appointment.Client)
                     .DistinctBy(client => client.Id)
                     .OrderBy(_ => random.Next())
                     .Take(6))
        {
            var service = Pick(random, services);
            result.Add(new Payment
            {
                Id = DeterministicId("payment", ordinal++),
                Client = client,
                Service = service.Entity,
                Amount = PriceAt(service.Prices, nowUtc),
                Date = nowUtc,
                Description = "Предоплата занятия"
            });
        }

        return result;
    }

    private static List<Expense> CreateExpenses(
        Random random,
        IReadOnlyList<ExpenseCategory> categories,
        DateOnly firstLocalDate,
        DateOnly lastLocalDate,
        TimeZoneInfo timeZone)
    {
        var descriptions = new Dictionary<string, string[]>
        {
            ["Аренда"] = ["Аренда студии", "Аренда кабинета"],
            ["Реклама"] = ["Продвижение объявлений", "Печать листовок", "Реклама в соцсетях"],
            ["Оборудование"] = ["Стойка для микрофона", "Педаль для инструмента", "Наушники"],
            ["Расходники"] = ["Ноты и тетради", "Струны", "Канцелярия", "Вода для студии"]
        };
        var result = new List<Expense>();
        var ordinal = 0;

        for (var month = new DateOnly(firstLocalDate.Year, firstLocalDate.Month, 1);
             month <= lastLocalDate;
             month = month.AddMonths(1))
        {
            var firstDay = month.Year == firstLocalDate.Year && month.Month == firstLocalDate.Month
                ? firstLocalDate.Day
                : 1;
            var lastDay = month.Year == lastLocalDate.Year && month.Month == lastLocalDate.Month
                ? lastLocalDate.Day
                : DateTime.DaysInMonth(month.Year, month.Month);
            var rentDate = new DateOnly(month.Year, month.Month, Math.Clamp(5, firstDay, lastDay));
            result.Add(new Expense
            {
                Id = DeterministicId("expense", ordinal++),
                Category = categories[0],
                CategoryId = categories[0].Id,
                Description = "Аренда студии",
                Amount = random.Next(28, 46) * 1_000m,
                Date = ConvertLocalToUtc(rentDate, 11, timeZone)
            });

            var otherCount = random.Next(3, 7);
            for (var index = 0; index < otherCount; index++)
            {
                var category = categories[random.Next(1, categories.Count)];
                var date = new DateOnly(month.Year, month.Month, random.Next(firstDay, lastDay + 1));
                result.Add(new Expense
                {
                    Id = DeterministicId("expense", ordinal++),
                    Category = category,
                    CategoryId = category.Id,
                    Description = Pick(random, descriptions[category.Name]),
                    Amount = random.Next(4, 80) * 250m,
                    Date = ConvertLocalToUtc(date, random.Next(9, 20), timeZone)
                });
            }
        }

        return result;
    }

    private static void EnsureDebtLimit(
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<Payment> existingPayments,
        List<Payment> newPayments,
        IReadOnlyList<FullDemoService> services,
        DateTime nowUtc)
    {
        var priceHistory = services.ToDictionary(service => service.Entity.Id, service => service.Prices);
        var billableAppointments = appointments
            .Where(appointment => !appointment.IsDeleted
                && appointment.StartDate < nowUtc
                && appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Burned
                && priceHistory.ContainsKey(appointment.Service.Id))
            .GroupBy(appointment => appointment.Client.Id);
        var repairOrdinal = 0;

        foreach (var clientAppointments in billableAppointments)
        {
            var ordered = clientAppointments.OrderBy(appointment => appointment.StartDate).ToArray();
            var charges = ordered
                .Select(appointment => PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate))
                .ToArray();
            var paid = existingPayments
                .Where(payment => payment.Client.Id == clientAppointments.Key)
                .Sum(payment => payment.Amount)
                + newPayments
                    .Where(payment => payment.Client.Id == clientAppointments.Key)
                    .Sum(payment => payment.Amount);
            var outstanding = Math.Max(0m, charges.Sum() - paid);
            var allowedOutstanding = charges.TakeLast(2).Sum();
            if (outstanding <= allowedOutstanding)
            {
                continue;
            }

            var lastAppointment = ordered[^1];
            newPayments.Add(new Payment
            {
                Id = DeterministicId("debt-repair-payment", repairOrdinal++),
                Client = lastAppointment.Client,
                Service = lastAppointment.Service,
                Amount = outstanding - allowedOutstanding,
                Date = nowUtc,
                Description = "Погашение задолженности"
            });
        }
    }

    private async Task<List<AppointmentRecurrenceRule>> CreateRecurrenceRulesAsync(
        Random random,
        IReadOnlyList<Client> clients,
        IReadOnlyList<FullDemoService> services,
        IReadOnlyList<ProviderSlot> candidateSlots,
        IReadOnlyList<Appointment> existingAppointments,
        IReadOnlyList<Appointment> newAppointments,
        DateTime nowUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var weekly = await db.RecurrenceTypes.SingleAsync(
            type => type.Type == AppointmentRecurrenceType.Weekly,
            cancellationToken);
        var occupied = existingAppointments
            .Concat(newAppointments)
            .Where(appointment => !appointment.IsDeleted && appointment.Provider is not null)
            .Select(appointment => new ScheduledInterval(appointment.Provider!.Id, appointment.StartDate, appointment.EndDate))
            .ToList();
        var availableSlots = candidateSlots
            .Where(slot => slot.StartUtc >= nowUtc
                && !Overlaps(occupied, slot.Provider.Id, slot.StartUtc, slot.StartUtc.AddHours(1)))
            .OrderBy(_ => random.Next())
            .ToList();
        var localYear = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone).Year;
        var endDate = ConvertLocalToUtc(new DateOnly(localYear, 12, 31), 23, timeZone).AddMinutes(59);
        var result = new List<AppointmentRecurrenceRule>();

        foreach (var client in clients.Where(client => !client.IsLeadClosed).OrderBy(_ => random.Next()).Take(6))
        {
            var slot = availableSlots.FirstOrDefault(candidate =>
                !Overlaps(occupied, candidate.Provider.Id, candidate.StartUtc, candidate.StartUtc.AddHours(1)));
            if (slot is null)
            {
                break;
            }

            var weekdayBit = 1 << (((int)slot.LocalDate.DayOfWeek + 6) % 7);
            result.Add(new AppointmentRecurrenceRule
            {
                Id = DeterministicId("recurrence-rule", result.Count),
                Client = client,
                Service = Pick(random, services).Entity,
                Provider = slot.Provider,
                StartDate = slot.StartUtc,
                EndDate = endDate,
                RecurrenceType = weekly,
                RecurrencePattern = weekdayBit
            });
            occupied.Add(new ScheduledInterval(slot.Provider.Id, slot.StartUtc, slot.StartUtc.AddHours(1)));
        }

        return result;
    }

    private static void ValidateSeedData(
        IReadOnlyList<Appointment> newAppointments,
        IReadOnlyList<Appointment> existingAppointments,
        IReadOnlyList<Payment> newPayments,
        IReadOnlyList<Payment> existingPayments,
        IReadOnlyList<AppointmentRecurrenceRule> recurrenceRules,
        IReadOnlyList<FullDemoService> services,
        IReadOnlyDictionary<Ulid, ProviderAvailability> availabilities,
        TimeZoneInfo timeZone,
        DateTime nowUtc)
    {
        foreach (var appointment in newAppointments)
        {
            var provider = appointment.Provider
                ?? throw new InvalidOperationException($"Development appointment {appointment.Id} has no provider.");
            if (appointment.EndDate - appointment.StartDate != TimeSpan.FromHours(1)
                || TimeZoneInfo.ConvertTimeFromUtc(appointment.StartDate, timeZone).Minute != 0
                || !IsAvailable(availabilities[provider.Id], appointment.StartDate, appointment.EndDate, timeZone))
            {
                throw new InvalidOperationException($"Development appointment {appointment.Id} violates schedule invariants.");
            }
        }

        var intervals = existingAppointments
            .Concat(newAppointments)
            .Where(appointment => !appointment.IsDeleted && appointment.Provider is not null)
            .Select(appointment => new ScheduledInterval(appointment.Provider!.Id, appointment.StartDate, appointment.EndDate))
            .Concat(recurrenceRules.Select(rule => new ScheduledInterval(
                rule.Provider!.Id,
                rule.StartDate,
                rule.StartDate.AddHours(1))));
        foreach (var providerIntervals in intervals.GroupBy(interval => interval.ProviderId))
        {
            var ordered = providerIntervals.OrderBy(interval => interval.StartUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index - 1].EndUtc > ordered[index].StartUtc)
                {
                    throw new InvalidOperationException($"Development provider {providerIntervals.Key} has overlapping seed appointments.");
                }
            }
        }

        foreach (var rule in recurrenceRules)
        {
            var provider = rule.Provider
                ?? throw new InvalidOperationException($"Development recurrence rule {rule.Id} has no provider.");
            if (!IsAvailable(availabilities[provider.Id], rule.StartDate, rule.StartDate.AddHours(1), timeZone))
            {
                throw new InvalidOperationException($"Development recurrence rule {rule.Id} violates provider availability.");
            }
        }

        ValidateClientDebt(
            existingAppointments.Concat(newAppointments).ToList(),
            existingPayments.Concat(newPayments).ToList(),
            services,
            nowUtc);
    }

    private static void ValidateClientDebt(
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<FullDemoService> services,
        DateTime nowUtc)
    {
        var priceHistory = services.ToDictionary(service => service.Entity.Id, service => service.Prices);
        foreach (var clientAppointments in appointments
                     .Where(appointment => !appointment.IsDeleted
                         && appointment.StartDate < nowUtc
                         && appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Burned
                         && priceHistory.ContainsKey(appointment.Service.Id))
                     .GroupBy(appointment => appointment.Client.Id))
        {
            var charges = clientAppointments
                .OrderBy(appointment => appointment.StartDate)
                .Select(appointment => PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate))
                .ToArray();
            foreach (var payment in payments
                         .Where(payment => payment.Client.Id == clientAppointments.Key)
                         .OrderBy(payment => payment.Date))
            {
                var remaining = payment.Amount;
                for (var index = 0; index < charges.Length && remaining > 0; index++)
                {
                    var allocated = Math.Min(charges[index], remaining);
                    charges[index] -= allocated;
                    remaining -= allocated;
                }
            }

            if (charges.Count(charge => charge > 0) > 2)
            {
                throw new InvalidOperationException($"Development client {clientAppointments.Key} has more than two unpaid lessons.");
            }
        }
    }

    private static ProviderAvailability CreateAvailability(User provider)
    {
        var workingHours = provider.WorkingHours.Count > 0
            ? provider.WorkingHours
                .Select(day => new WorkingDay(day.DayOfWeek, day.IsWorkingDay, day.StartMinuteOfDay, day.EndMinuteOfDay))
                .ToList()
            : DefaultWorkingHours();
        return new ProviderAvailability(workingHours, provider.Vacations);
    }

    private static bool IsAvailable(
        ProviderAvailability availability,
        DateTime startUtc,
        DateTime endUtc,
        TimeZoneInfo timeZone)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, timeZone);
        if (localStart.Date != localEnd.Date
            || availability.Vacations.Any(vacation => startUtc < vacation.EndDate && endUtc > vacation.StartDate))
        {
            return false;
        }

        var workingDay = availability.WorkingHours.FirstOrDefault(day => day.DayOfWeek == localStart.DayOfWeek);
        if (workingDay is null)
        {
            return false;
        }

        var startMinute = localStart.Hour * 60 + localStart.Minute;
        var endMinute = localEnd.Hour * 60 + localEnd.Minute;
        return workingDay.IsWorkingDay
            && startMinute >= workingDay.StartMinuteOfDay
            && endMinute <= workingDay.EndMinuteOfDay;
    }

    private static List<WorkingDay> DefaultWorkingHours()
    {
        return
        [
            new(DayOfWeek.Monday, true, 600, 1200),
            new(DayOfWeek.Tuesday, true, 600, 1200),
            new(DayOfWeek.Wednesday, true, 600, 1200),
            new(DayOfWeek.Thursday, true, 600, 1200),
            new(DayOfWeek.Friday, true, 600, 1200),
            new(DayOfWeek.Saturday, false, 600, 1200),
            new(DayOfWeek.Sunday, false, 600, 1200)
        ];
    }

    private static bool Overlaps(
        IEnumerable<ScheduledInterval> intervals,
        Ulid providerId,
        DateTime startUtc,
        DateTime endUtc)
    {
        return intervals.Any(interval => interval.ProviderId == providerId
            && interval.StartUtc < endUtc
            && startUtc < interval.EndUtc);
    }

    private static AppointmentStatus PastAppointmentStatus(Random random)
    {
        var value = random.NextDouble();
        return value switch
        {
            < 0.82 => AppointmentStatus.Completed,
            < 0.91 => AppointmentStatus.Cancelled,
            _ => AppointmentStatus.Burned
        };
    }

    private static decimal PriceAt(IReadOnlyList<ServicePrice> prices, DateTime date)
    {
        return prices.Where(price => price.EffectiveDate <= date).OrderBy(price => price.EffectiveDate).Last().Price;
    }

    private static T Pick<T>(Random random, IReadOnlyList<T> items)
    {
        return items[random.Next(items.Count)];
    }

    private static DateOnly EarlierDate(DateOnly left, DateOnly right)
    {
        return left <= right ? left : right;
    }

    private static DateTime ConvertLocalToUtc(DateOnly date, int hour, TimeZoneInfo timeZone)
    {
        return TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified),
            timeZone);
    }

    private static Ulid LegacyClientId(int index)
    {
        return DeterministicV3Id("client", index);
    }

    private static Ulid DeterministicV3Id(string entity, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"melodytrack-development-v3:{entity}:{index}"));
        return new Ulid(hash[..16]);
    }

    private static Ulid DeterministicId(string entity, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"melodytrack-development-v6:{entity}:{index}"));
        return new Ulid(hash[..16]);
    }

    private static string Transliterate(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            result.Append(character switch
            {
                'а' => "a",
                'б' => "b",
                'в' => "v",
                'г' => "g",
                'д' => "d",
                'е' or 'ё' => "e",
                'ж' => "zh",
                'з' => "z",
                'и' or 'й' => "i",
                'к' => "k",
                'л' => "l",
                'м' => "m",
                'н' => "n",
                'о' => "o",
                'п' => "p",
                'р' => "r",
                'с' => "s",
                'т' => "t",
                'у' => "u",
                'ф' => "f",
                'х' => "h",
                'ц' => "c",
                'ч' => "ch",
                'ш' => "sh",
                'щ' => "sch",
                'ы' => "y",
                'э' => "e",
                'ю' => "yu",
                'я' => "ya",
                _ => character.ToString()
            });
        }

        return result.ToString();
    }

    private sealed record ServiceDefinition(
        string Name,
        string PublicName,
        string Description,
        bool IsConsultation,
        decimal BasePrice);

    private sealed record FullDemoService(Service Entity, List<ServicePrice> Prices);
    private sealed record ProviderSlot(User Provider, DateOnly LocalDate, int StartMinuteOfDay, DateTime StartUtc);
    private sealed record ScheduledInterval(Ulid ProviderId, DateTime StartUtc, DateTime EndUtc);
    private sealed record WorkingDay(DayOfWeek DayOfWeek, bool IsWorkingDay, int StartMinuteOfDay, int EndMinuteOfDay);
    private sealed record ProviderAvailability(IReadOnlyList<WorkingDay> WorkingHours, IReadOnlyList<UserVacation> Vacations);
}
