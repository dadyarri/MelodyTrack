#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../MelodyTrack.Backend/MelodyTrack.Backend.csproj
#:package Bogus@35.6.5

using System.Data;
using System.Globalization;
using System.Text.Json;
using Bogus;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

try
{
    var backendRoot = FindBackendRoot();
    LoadDevelopmentEnvironment(backendRoot);

    var nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
    var options = SeedOptions.Parse(args, nowUtc.Year);
    if (options.ShowHelp)
    {
        SeedOptions.PrintHelp();
        return 0;
    }

    var databaseUrl = GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
    EnsureLocalDatabase(databaseUrl);

    var piiKeyVersion = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY_VERSION") ?? "v1";
    var piiKeys = LoadPiiKeys(piiKeyVersion, GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY"));
    var protector = new PersonalDataProtector(piiKeyVersion, piiKeys);
    var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(databaseUrl)
        .Options;

    await using var db = new AppDbContext(dbOptions, protector);
    if (!await db.Database.CanConnectAsync())
    {
        throw new InvalidOperationException("Не удалось подключиться к локальной базе данных.");
    }

    var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    if (pendingMigrations.Length > 0)
    {
        throw new InvalidOperationException(
            $"В базе есть неприменённые миграции ({pendingMigrations.Length}). Сначала запустите приложение и примените миграции.");
    }

    var providers = await db.Users
        .Include(user => user.Role)
        .Where(user => user.Role.RoleName != UserRoles.Client)
        .OrderBy(user => user.LastName)
        .ThenBy(user => user.FirstName)
        .Take(4)
        .ToListAsync();

    if (providers.Count == 0)
    {
        throw new InvalidOperationException(
            "Сначала создайте хотя бы одного пользователя приложения. Скрипт привязывает расписание к существующим сотрудникам.");
    }

    var availabilityService = new UserAvailabilityService(db);
    var availabilities = (await availabilityService.GetAvailabilitiesAsync(
            providers.Select(provider => provider.Id).ToArray(),
            CancellationToken.None))
        .ToDictionary(availability => availability.UserId);

    Randomizer.Seed = new Random(options.Seed);
    var faker = new Faker("ru");
    var random = new Random(options.Seed);
    var localNow = DateTimeUtils.ConvertDateToTimezone(nowUtc, options.TimeZoneId);
    var firstLocalDate = new DateOnly(options.Year, 1, 1);
    var lastLocalDate = options.Year == nowUtc.Year
        ? DateOnly.FromDateTime(localNow)
        : new DateOnly(options.Year, 12, 31);
    var scheduleEndLocalDate = options.Year == nowUtc.Year
        ? EarlierDate(lastLocalDate.AddDays(21), new DateOnly(options.Year, 12, 31))
        : lastLocalDate;
    var periodEnd = DateTimeUtils.ConvertLocalDateToUtc(lastLocalDate.AddDays(1), TimeOnly.MinValue, options.TimeZoneId).AddTicks(-1);

    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    if (await HasPrimaryDataAsync(db))
    {
        Console.WriteLine("Целевые таблицы уже содержат данные. Новые строки не добавлены.");
        return 0;
    }

    var sources = await ResolveSourcesAsync(db);
    var services = CreateServices(options.Year, periodEnd, options.TimeZoneId);
    var categories = await ResolveExpenseCategoriesAsync(db);
    var clients = CreateClients(
        faker,
        random,
        sources,
        options.ClientCount,
        firstLocalDate,
        lastLocalDate,
        options.TimeZoneId);

    await db.Services.AddRangeAsync(services.Select(item => item.Service));
    await db.ServicePriceHistory.AddRangeAsync(services.SelectMany(item => item.Prices));
    await db.Clients.AddRangeAsync(clients);

    var candidateSlots = CreateCandidateSlots(
        providers,
        availabilities,
        firstLocalDate,
        scheduleEndLocalDate,
        options.TimeZoneId);
    if (candidateSlots.Count == 0)
    {
        throw new InvalidOperationException("В выбранном периоде нет ни одного доступного часового окна преподавателя.");
    }

    var occupiedSlots = new HashSet<ProviderSlotKey>();
    var appointments = CreateAppointments(
        random,
        clients,
        services,
        candidateSlots,
        occupiedSlots,
        nowUtc);
    var payments = CreatePayments(random, appointments, services, nowUtc, periodEnd);
    var expenses = CreateExpenses(
        random,
        categories,
        options.Year,
        lastLocalDate,
        options.TimeZoneId);

    await db.Appointments.AddRangeAsync(appointments);
    await db.Payments.AddRangeAsync(payments);
    await db.Expenses.AddRangeAsync(expenses);

    var recurrenceRules = new List<AppointmentRecurrenceRule>();
    if (options.Year == nowUtc.Year)
    {
        var weekly = await db.RecurrenceTypes.SingleAsync(type => type.Type == AppointmentRecurrenceType.Weekly);
        recurrenceRules = CreateRecurrenceRules(
            random,
            clients,
            services,
            candidateSlots,
            occupiedSlots,
            weekly,
            nowUtc,
            options.TimeZoneId);
        await db.RecurrenceRules.AddRangeAsync(recurrenceRules);
    }

    ValidateSeedData(
        appointments,
        payments,
        recurrenceRules,
        services,
        availabilities,
        options.TimeZoneId,
        nowUtc);

    if (clients.Count == 0 || services.Count == 0 || appointments.Count == 0 || payments.Count == 0 || expenses.Count == 0)
    {
        throw new InvalidOperationException("Набор данных должен содержать клиентов, услуги, занятия, оплаты и расходы.");
    }

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    var connection = new NpgsqlConnectionStringBuilder(databaseUrl);
    Console.WriteLine($"Готово: локальная база {connection.Database} на {connection.Host} заполнена с {firstLocalDate:dd.MM.yyyy}.");
    Console.WriteLine($"Часовой пояс расписания: {options.TimeZoneId}.");
    Console.WriteLine($"Клиенты: {clients.Count}; занятия: {appointments.Count}; оплаты: {payments.Count}; расходы: {expenses.Count}; будущие правила: {recurrenceRules.Count}.");
    Console.WriteLine($"Преподаватели в расписании: {string.Join(", ", providers.Select(user => $"{user.FirstName} {user.LastName}"))}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Не удалось заполнить базу: {exception.Message}");
    return 1;
}

static async Task<bool> HasPrimaryDataAsync(AppDbContext db)
{
    return await db.Appointments.AsNoTracking().AnyAsync()
           || await db.Clients.AsNoTracking().AnyAsync()
           || await db.Payments.AsNoTracking().AnyAsync()
           || await db.Expenses.AsNoTracking().AnyAsync()
           || await db.Services.AsNoTracking().AnyAsync();
}

static async Task<List<ClientSource>> ResolveSourcesAsync(AppDbContext db)
{
    string[] names = ["Рекомендации", "ВКонтакте", "Яндекс Карты", "Сайт", "Вывеска"];
    var existing = await db.ClientSources
        .Where(source => names.Contains(source.Name))
        .ToDictionaryAsync(source => source.Name, StringComparer.Ordinal);
    var result = new List<ClientSource>(names.Length);

    foreach (var name in names)
    {
        if (!existing.TryGetValue(name, out var source))
        {
            source = Entity(new ClientSource { Name = name });
            db.ClientSources.Add(source);
        }

        result.Add(source);
    }

    return result;
}

static List<SeedService> CreateServices(int year, DateTime periodEnd, string timezoneId)
{
    var definitions = new[]
    {
        new ServiceDefinition("Вокал", "Вокал", "Индивидуальное занятие по вокалу", false, 2_200m),
        new ServiceDefinition("Фортепиано", "Фортепиано", "Индивидуальное занятие по фортепиано", false, 2_400m),
        new ServiceDefinition("Гитара", "Гитара", "Индивидуальное занятие по гитаре", false, 2_100m),
        new ServiceDefinition("Сольфеджио", "Сольфеджио", "Занятие по музыкальной теории", false, 1_800m),
        new ServiceDefinition("Знакомство", "Пробное занятие", "Первое знакомство с преподавателем", true, 1_000m)
    };
    var priceDates = new[]
    {
        new DateOnly(year, 1, 1),
        new DateOnly(year, 4, 1),
        new DateOnly(year, 7, 1)
    };

    return definitions.Select(definition =>
    {
        var service = Entity(new Service
        {
            Name = definition.Name,
            PublicName = definition.PublicName,
            Description = definition.Description,
            IsConsultation = definition.IsConsultation
        });
        var prices = priceDates
            .Select(date => DateTimeUtils.ConvertLocalDateToUtc(date, TimeOnly.MinValue, timezoneId))
            .Where(date => date <= periodEnd)
            .Select((date, index) => Entity(new ServicePrice
            {
                Service = service,
                EffectiveDate = date,
                Price = definition.BasePrice + index * 200m
            }))
            .ToList();
        return new SeedService(service, prices);
    }).ToList();
}

static async Task<List<ExpenseCategory>> ResolveExpenseCategoriesAsync(AppDbContext db)
{
    string[] names = ["Аренда", "Реклама", "Оборудование", "Расходники"];
    var existing = await db.ExpenseCategories
        .Where(category => names.Contains(category.Name))
        .ToDictionaryAsync(category => category.Name, StringComparer.Ordinal);
    var result = new List<ExpenseCategory>(names.Length);

    foreach (var name in names)
    {
        if (!existing.TryGetValue(name, out var category))
        {
            category = Entity(new ExpenseCategory { Name = name });
            db.ExpenseCategories.Add(category);
        }

        result.Add(category);
    }

    return result;
}

static List<Client> CreateClients(
    Faker faker,
    Random random,
    IReadOnlyList<ClientSource> sources,
    int count,
    DateOnly firstLocalDate,
    DateOnly lastLocalDate,
    string timezoneId)
{
    var latestCreationDate = EarlierDate(lastLocalDate, firstLocalDate.AddMonths(4));
    var creationRange = Math.Max(1, latestCreationDate.DayNumber - firstLocalDate.DayNumber + 1);
    var clients = new List<Client>(count);

    for (var index = 0; index < count; index++)
    {
        var person = new Person("ru");
        var age = random.Next(8, 62);
        var birthDate = firstLocalDate.AddYears(-age).AddDays(-random.Next(0, 365));
        var createdDate = firstLocalDate.AddDays(random.Next(creationRange));
        var createdAt = DateTimeUtils.ConvertLocalDateToUtc(
            createdDate,
            new TimeOnly(random.Next(9, 19), random.Next(0, 60)),
            timezoneId);
        var telegramName = faker.Internet.UserName();
        var vkName = faker.Internet.UserName();

        clients.Add(Entity(new Client
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            DateOfBirth = birthDate,
            Source = Pick(random, sources),
            CreatedAtUtc = createdAt,
            IsLeadClosed = random.NextDouble() < 0.12,
            Contacts = Entity(new ClientContacts
            {
                Email = faker.Internet.Email(person.FirstName, person.LastName),
                Phone = faker.Phone.PhoneNumber("+7##########"),
                Telegram = $"https://t.me/{telegramName}",
                Vk = $"https://vk.com/{vkName}"
            })
        }));
    }

    return clients;
}

static List<ProviderSlot> CreateCandidateSlots(
    IReadOnlyList<User> providers,
    IReadOnlyDictionary<Ulid, UserAvailabilitySnapshot> availabilities,
    DateOnly firstLocalDate,
    DateOnly lastLocalDate,
    string timezoneId)
{
    var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    var slots = new List<ProviderSlot>();

    foreach (var provider in providers)
    {
        if (!availabilities.TryGetValue(provider.Id, out var availability))
        {
            throw new InvalidOperationException($"Не удалось загрузить доступность пользователя {provider.Id}.");
        }

        for (var date = firstLocalDate; date <= lastLocalDate; date = date.AddDays(1))
        {
            if (availability.Vacations.Any(vacation => vacation.StartDate <= date && vacation.EndDate >= date))
            {
                continue;
            }

            var workingDay = availability.WorkingHours.FirstOrDefault(day => day.DayOfWeek == date.DayOfWeek);
            if (workingDay is null || !workingDay.IsWorkingDay)
            {
                continue;
            }

            var firstMinute = ((workingDay.StartMinuteOfDay + 59) / 60) * 60;
            for (var minute = firstMinute; minute + 60 <= workingDay.EndMinuteOfDay; minute += 60)
            {
                var time = new TimeOnly(minute / 60, minute % 60);
                var localStart = date.ToDateTime(time, DateTimeKind.Unspecified);
                if (timezone.IsInvalidTime(localStart))
                {
                    continue;
                }

                var startUtc = DateTimeUtils.ConvertLocalDateToUtc(date, time, timezoneId);
                var endUtc = startUtc.AddHours(1);
                if (!UserAvailabilityService.IsAvailable(availability, startUtc, endUtc, timezoneId))
                {
                    continue;
                }

                slots.Add(new ProviderSlot(provider, date, minute, startUtc));
            }
        }
    }

    return slots;
}

static List<Appointment> CreateAppointments(
    Random random,
    IReadOnlyList<Client> clients,
    IReadOnlyList<SeedService> services,
    IReadOnlyList<ProviderSlot> candidateSlots,
    ISet<ProviderSlotKey> occupiedSlots,
    DateTime nowUtc)
{
    string[] notes =
    [
        "Повторить упражнение на дыхание.",
        "Хорошо разобрали домашнее задание.",
        "На следующем занятии продолжить с середины произведения.",
        "Обратить внимание на ритм.",
        "Закрепить материал дома."
    ];
    var appointments = new List<Appointment>();

    foreach (var daySlots in candidateSlots.GroupBy(slot => slot.LocalDate).OrderBy(group => group.Key))
    {
        var availableSlots = daySlots
            .Where(slot => !occupiedSlots.Contains(slot.Key))
            .OrderBy(_ => random.Next())
            .ToArray();
        var eligibleClients = clients
            .Where(client => !client.IsLeadClosed && availableSlots.Any(slot => client.CreatedAtUtc <= slot.StartUtc))
            .ToArray();
        if (availableSlots.Length == 0 || eligibleClients.Length == 0)
        {
            continue;
        }

        var maximumDailyCount = Math.Min(7, Math.Min(availableSlots.Length, eligibleClients.Length));
        var minimumDailyCount = Math.Min(3, maximumDailyCount);
        var dailyCount = random.Next(minimumDailyCount, maximumDailyCount + 1);

        foreach (var slot in availableSlots.Take(dailyCount))
        {
            var clientsCreatedBySlot = eligibleClients.Where(client => client.CreatedAtUtc <= slot.StartUtc).ToArray();
            if (clientsCreatedBySlot.Length == 0)
            {
                continue;
            }

            var service = Pick(random, services);
            var status = slot.StartUtc >= nowUtc
                ? AppointmentStatus.Planned
                : PastAppointmentStatus(random);

            appointments.Add(Entity(new Appointment
            {
                Client = Pick(random, clientsCreatedBySlot),
                Service = service.Service,
                Provider = slot.Provider,
                StartDate = slot.StartUtc,
                EndDate = slot.StartUtc.AddHours(1),
                Status = status,
                IsDeleted = slot.StartUtc < nowUtc && random.NextDouble() < 0.012,
                LessonNotes = status == AppointmentStatus.Completed && random.NextDouble() < 0.32
                    ? Pick(random, notes)
                    : null
            }));
            occupiedSlots.Add(slot.Key);
        }
    }

    return appointments;
}

static List<Payment> CreatePayments(
    Random random,
    IReadOnlyList<Appointment> appointments,
    IReadOnlyList<SeedService> services,
    DateTime nowUtc,
    DateTime periodEnd)
{
    var priceHistory = services.ToDictionary(
        item => item.Service.Id,
        item => (IReadOnlyList<ServicePrice>)item.Prices.OrderBy(price => price.EffectiveDate).ToList());
    var billableAppointments = appointments
        .Where(item =>
            !item.IsDeleted
            && item.StartDate < nowUtc
            && item.Status is AppointmentStatus.Completed or AppointmentStatus.Burned)
        .OrderBy(item => item.StartDate)
        .ToList();
    var payments = new List<Payment>();

    foreach (var clientAppointments in billableAppointments.GroupBy(appointment => appointment.Client.Id))
    {
        var orderedAppointments = clientAppointments.OrderBy(appointment => appointment.StartDate).ToArray();
        var outstandingCount = random.Next(0, Math.Min(2, orderedAppointments.Length) + 1);

        foreach (var appointment in orderedAppointments.Take(orderedAppointments.Length - outstandingCount))
        {
            var paidAt = appointment.StartDate.AddDays(random.Next(0, 8)).AddHours(random.Next(0, 5));
            paidAt = EarlierDateTime(EarlierDateTime(paidAt, nowUtc), periodEnd);
            payments.Add(Entity(new Payment
            {
                Client = appointment.Client,
                Service = appointment.Service,
                Amount = PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate),
                Date = paidAt,
                Description = "Оплата занятия"
            }));
        }
    }

    foreach (var client in appointments
                 .Where(item => item.StartDate >= nowUtc && item.StartDate <= nowUtc.AddDays(21))
                 .Select(item => item.Client)
                 .DistinctBy(client => client.Id)
                 .OrderBy(_ => random.Next())
                 .Take(6))
    {
        var service = Pick(random, services);
        payments.Add(Entity(new Payment
        {
            Client = client,
            Service = service.Service,
            Amount = PriceAt(service.Prices, nowUtc),
            Date = nowUtc,
            Description = "Предоплата занятия"
        }));
    }

    if (payments.Count == 0 && billableAppointments.Count > 0)
    {
        var appointment = billableAppointments[0];
        payments.Add(Entity(new Payment
        {
            Client = appointment.Client,
            Service = appointment.Service,
            Amount = PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate),
            Date = EarlierDateTime(nowUtc, periodEnd),
            Description = "Оплата занятия"
        }));
    }

    return payments;
}

static List<Expense> CreateExpenses(
    Random random,
    IReadOnlyList<ExpenseCategory> categories,
    int year,
    DateOnly lastLocalDate,
    string timezoneId)
{
    var descriptions = new Dictionary<string, string[]>
    {
        ["Аренда"] = ["Аренда студии", "Аренда кабинета"],
        ["Реклама"] = ["Продвижение объявлений", "Печать листовок", "Реклама в соцсетях"],
        ["Оборудование"] = ["Стойка для микрофона", "Педаль для инструмента", "Наушники"],
        ["Расходники"] = ["Ноты и тетради", "Струны", "Канцелярия", "Вода для студии"]
    };
    var expenses = new List<Expense>();

    for (var month = new DateOnly(year, 1, 1); month <= lastLocalDate; month = month.AddMonths(1))
    {
        var lastDay = month.Year == lastLocalDate.Year && month.Month == lastLocalDate.Month
            ? lastLocalDate.Day
            : DateTime.DaysInMonth(month.Year, month.Month);
        var monthlyRent = categories[0];
        expenses.Add(Entity(new Expense
        {
            Category = monthlyRent,
            CategoryId = monthlyRent.Id,
            Description = "Аренда студии",
            Amount = random.Next(28, 46) * 1_000m,
            Date = DateTimeUtils.ConvertLocalDateToUtc(
                new DateOnly(month.Year, month.Month, Math.Min(5, lastDay)),
                new TimeOnly(11, 0),
                timezoneId)
        }));

        var otherCount = random.Next(3, 7);
        for (var index = 0; index < otherCount; index++)
        {
            var category = categories[random.Next(1, categories.Count)];
            expenses.Add(Entity(new Expense
            {
                Category = category,
                CategoryId = category.Id,
                Description = Pick(random, descriptions[category.Name]),
                Amount = random.Next(4, 80) * 250m,
                Date = DateTimeUtils.ConvertLocalDateToUtc(
                    new DateOnly(month.Year, month.Month, random.Next(1, lastDay + 1)),
                    new TimeOnly(random.Next(9, 20), 0),
                    timezoneId)
            }));
        }
    }

    return expenses;
}

static List<AppointmentRecurrenceRule> CreateRecurrenceRules(
    Random random,
    IReadOnlyList<Client> clients,
    IReadOnlyList<SeedService> services,
    IReadOnlyList<ProviderSlot> candidateSlots,
    ISet<ProviderSlotKey> occupiedSlots,
    RecurrenceType weekly,
    DateTime nowUtc,
    string timezoneId)
{
    var activeClients = clients.Where(client => !client.IsLeadClosed).OrderBy(_ => random.Next()).Take(6).ToArray();
    var availableFutureSlots = candidateSlots
        .Where(slot => slot.StartUtc >= nowUtc && !occupiedSlots.Contains(slot.Key))
        .OrderBy(_ => random.Next())
        .ToList();
    var rules = new List<AppointmentRecurrenceRule>();
    var endDate = DateTimeUtils.ConvertLocalDateToUtc(
        new DateOnly(nowUtc.Year, 12, 31),
        new TimeOnly(23, 59),
        timezoneId);

    foreach (var client in activeClients)
    {
        var slot = availableFutureSlots.FirstOrDefault(candidate => !occupiedSlots.Contains(candidate.Key));
        if (slot is null)
        {
            break;
        }

        occupiedSlots.Add(slot.Key);
        var weekdayBit = 1 << (((int)slot.LocalDate.DayOfWeek + 6) % 7);
        rules.Add(Entity(new AppointmentRecurrenceRule
        {
            Client = client,
            Service = Pick(random, services).Service,
            Provider = slot.Provider,
            StartDate = slot.StartUtc,
            EndDate = endDate,
            RecurrenceType = weekly,
            RecurrencePattern = weekdayBit
        }));
    }

    return rules;
}

static void ValidateSeedData(
    IReadOnlyList<Appointment> appointments,
    IReadOnlyList<Payment> payments,
    IReadOnlyList<AppointmentRecurrenceRule> recurrenceRules,
    IReadOnlyList<SeedService> services,
    IReadOnlyDictionary<Ulid, UserAvailabilitySnapshot> availabilities,
    string timezoneId,
    DateTime nowUtc)
{
    foreach (var appointment in appointments)
    {
        if (appointment.EndDate - appointment.StartDate != TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException($"Занятие {appointment.Id} длится не один час.");
        }

        var localStart = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, timezoneId);
        if (localStart.Minute != 0 || localStart.Second != 0 || localStart.Millisecond != 0)
        {
            throw new InvalidOperationException($"Занятие {appointment.Id} начинается не в начале локального часа.");
        }

        var provider = appointment.Provider
                       ?? throw new InvalidOperationException($"У занятия {appointment.Id} не указан преподаватель.");
        if (!availabilities.TryGetValue(provider.Id, out var availability)
            || !UserAvailabilityService.IsAvailable(availability, appointment.StartDate, appointment.EndDate, timezoneId))
        {
            throw new InvalidOperationException($"Занятие {appointment.Id} выходит за доступность преподавателя.");
        }
    }

    var scheduledIntervals = appointments
        .Where(item => !item.IsDeleted)
        .Select(item => new ScheduledInterval(item.Provider!.Id, item.StartDate, item.EndDate, $"занятие {item.Id}"))
        .Concat(recurrenceRules.Select(rule => new ScheduledInterval(
            rule.Provider!.Id,
            rule.StartDate,
            rule.StartDate.AddHours(1),
            $"правило {rule.Id}")));

    foreach (var providerIntervals in scheduledIntervals.GroupBy(item => item.ProviderId))
    {
        var ordered = providerIntervals.OrderBy(item => item.StartUtc).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].StartUtc < ordered[index].EndUtc
                && ordered[index].StartUtc < ordered[index - 1].EndUtc)
            {
                throw new InvalidOperationException(
                    $"У преподавателя {providerIntervals.Key} пересекаются {ordered[index - 1].Description} и {ordered[index].Description}.");
            }
        }
    }

    foreach (var rule in recurrenceRules)
    {
        var localStart = DateTimeUtils.ConvertDateToTimezone(rule.StartDate, timezoneId);
        var provider = rule.Provider
                       ?? throw new InvalidOperationException($"У правила {rule.Id} не указан преподаватель.");
        if (localStart.Minute != 0 || localStart.Second != 0
            || !availabilities.TryGetValue(provider.Id, out var availability)
            || !UserAvailabilityService.IsAvailable(availability, rule.StartDate, rule.StartDate.AddHours(1), timezoneId))
        {
            throw new InvalidOperationException($"Начало правила {rule.Id} не соответствует доступности преподавателя.");
        }
    }

    ValidateClientDebt(appointments, payments, services, nowUtc);
}

static void ValidateClientDebt(
    IReadOnlyList<Appointment> appointments,
    IReadOnlyList<Payment> payments,
    IReadOnlyList<SeedService> services,
    DateTime nowUtc)
{
    var priceHistory = services.ToDictionary(
        item => item.Service.Id,
        item => (IReadOnlyList<ServicePrice>)item.Prices.OrderBy(price => price.EffectiveDate).ToList());
    var billableAppointments = appointments
        .Where(item =>
            !item.IsDeleted
            && item.StartDate < nowUtc
            && item.Status is AppointmentStatus.Completed or AppointmentStatus.Burned)
        .GroupBy(item => item.Client.Id);

    foreach (var clientAppointments in billableAppointments)
    {
        var ledgers = clientAppointments
            .OrderBy(item => item.StartDate)
            .Select(item => PriceAt(priceHistory[item.Service.Id], item.StartDate))
            .ToArray();
        var remainingPayments = payments
            .Where(payment => payment.Client.Id == clientAppointments.Key)
            .OrderBy(payment => payment.Date)
            .Select(payment => payment.Amount);
        var remainders = ledgers.ToArray();

        foreach (var paymentAmount in remainingPayments)
        {
            var remaining = paymentAmount;
            for (var index = 0; index < remainders.Length && remaining > 0; index++)
            {
                var allocated = Math.Min(remainders[index], remaining);
                remainders[index] -= allocated;
                remaining -= allocated;
            }
        }

        if (remainders.Count(remainder => remainder > 0) > 2)
        {
            throw new InvalidOperationException($"У клиента {clientAppointments.Key} осталось больше двух неоплаченных занятий.");
        }
    }
}

static AppointmentStatus PastAppointmentStatus(Random random)
{
    var value = random.NextDouble();
    return value switch
    {
        < 0.82 => AppointmentStatus.Completed,
        < 0.91 => AppointmentStatus.Cancelled,
        _ => AppointmentStatus.Burned
    };
}

static decimal PriceAt(IReadOnlyList<ServicePrice> history, DateTime date)
{
    return history
        .Where(price => price.EffectiveDate <= date)
        .OrderBy(price => price.EffectiveDate)
        .Last()
        .Price;
}

static T Entity<T>(T entity) where T : BaseModel
{
    entity.Id = Ulid.NewUlid();
    return entity;
}

static T Pick<T>(Random random, IReadOnlyList<T> items)
{
    return items[random.Next(items.Count)];
}

static DateTime EarlierDateTime(DateTime left, DateTime right)
{
    return left <= right ? left : right;
}

static DateOnly EarlierDate(DateOnly left, DateOnly right)
{
    return left <= right ? left : right;
}

static void EnsureLocalDatabase(string connectionString)
{
    var connection = new NpgsqlConnectionStringBuilder(connectionString);
    var host = connection.Host?.Trim().Trim('[', ']') ?? throw new InvalidOperationException("В строке подключения не указан хост.");
    var allowedHosts = new[] { "localhost", "127.0.0.1", "::1", "host.docker.internal" };
    if (!allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Скрипт работает только с локальной базой. Хост «{host}» не входит в список разрешённых.");
    }

    if (string.IsNullOrWhiteSpace(connection.Database))
    {
        throw new InvalidOperationException("В строке подключения не указано имя базы данных.");
    }
}

static IReadOnlyDictionary<string, string> LoadPiiKeys(string currentVersion, string currentKey)
{
    var keys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [currentVersion] = currentKey
    };
    var configuredKeys = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEYS");
    if (string.IsNullOrWhiteSpace(configuredKeys))
    {
        return keys;
    }

    foreach (var pair in configuredKeys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var separator = pair.IndexOf('=');
        if (separator <= 0 || separator == pair.Length - 1)
        {
            throw new InvalidOperationException("MELODY_TRACK_PII_MASTER_KEYS должен иметь формат version=key;version2=key2.");
        }

        keys[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
    }

    return keys;
}

static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Не задана переменная окружения {name}.")
        : value;
}

static void LoadDevelopmentEnvironment(string backendRoot)
{
    var launchSettingsPath = Path.Combine(backendRoot, "MelodyTrack.Backend", "Properties", "launchSettings.json");
    using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
    var variables = document.RootElement
        .GetProperty("profiles")
        .GetProperty("http")
        .GetProperty("environmentVariables");

    foreach (var variable in variables.EnumerateObject())
    {
        if (Environment.GetEnvironmentVariable(variable.Name) is null)
        {
            Environment.SetEnvironmentVariable(variable.Name, variable.Value.GetString());
        }
    }
}

static string FindBackendRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (IsBackendRoot(directory.FullName))
        {
            return directory.FullName;
        }

        var nestedBackend = Path.Combine(directory.FullName, "MelodyTrack.Backend");
        if (IsBackendRoot(nestedBackend))
        {
            return nestedBackend;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Не удалось найти каталог MelodyTrack.Backend.");
}

static bool IsBackendRoot(string path)
{
    return File.Exists(Path.Combine(path, "MelodyTrack.slnx"))
           && File.Exists(Path.Combine(path, "MelodyTrack.Backend", "Properties", "launchSettings.json"));
}

internal sealed record ProviderSlot(User Provider, DateOnly LocalDate, int StartMinuteOfDay, DateTime StartUtc)
{
    public ProviderSlotKey Key => new(Provider.Id, LocalDate, StartMinuteOfDay);
}

internal sealed record ProviderSlotKey(Ulid ProviderId, DateOnly LocalDate, int StartMinuteOfDay);

internal sealed record ScheduledInterval(Ulid ProviderId, DateTime StartUtc, DateTime EndUtc, string Description);

internal sealed record SeedService(Service Service, List<ServicePrice> Prices);

internal sealed record ServiceDefinition(
    string Name,
    string PublicName,
    string Description,
    bool IsConsultation,
    decimal BasePrice);

internal sealed record SeedOptions(int Year, int ClientCount, int Seed, string TimeZoneId, bool ShowHelp)
{
    public static SeedOptions Parse(string[] arguments, int currentYear)
    {
        var year = currentYear;
        var clientCount = 48;
        int? seed = null;
        var timezoneId = TimeZoneInfo.Local.Id;
        var showHelp = arguments.Contains("--help", StringComparer.Ordinal) || arguments.Contains("-h", StringComparer.Ordinal);

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--help" or "-h":
                    break;
                case "--year":
                    year = ReadInt(arguments, ref index, "--year");
                    break;
                case "--clients":
                    clientCount = ReadInt(arguments, ref index, "--clients");
                    break;
                case "--seed":
                    seed = ReadInt(arguments, ref index, "--seed");
                    break;
                case "--timezone":
                    timezoneId = ReadString(arguments, ref index, "--timezone");
                    break;
                default:
                    throw new InvalidOperationException($"Неизвестный аргумент: {arguments[index]}. Используйте --help.");
            }
        }

        if (year is < 2020 || year > currentYear)
        {
            throw new InvalidOperationException($"--year должен быть от 2020 до {currentYear}.");
        }

        if (clientCount is < 10 or > 500)
        {
            throw new InvalidOperationException("--clients должен быть от 10 до 500.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException($"Часовой пояс «{timezoneId}» не найден.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Данные часового пояса «{timezoneId}» повреждены.");
        }

        return new SeedOptions(year, clientCount, seed ?? year * 100 + 1, timezoneId, showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Заполняет локальную базу MelodyTrack реалистичными русскоязычными данными начиная с января.");
        Console.WriteLine();
        Console.WriteLine("Использование:");
        Console.WriteLine("  dotnet run scripts/SeedLocalData.cs");
        Console.WriteLine("  dotnet run scripts/SeedLocalData.cs -- --year 2026 --clients 60 --seed 42 --timezone Europe/Moscow");
        Console.WriteLine();
        Console.WriteLine("Параметры:");
        Console.WriteLine("  --year <год>              год начала данных; по умолчанию текущий");
        Console.WriteLine("  --clients <число>         количество клиентов, от 10 до 500; по умолчанию 48");
        Console.WriteLine("  --seed <число>            начальное значение для повторяемой генерации");
        Console.WriteLine($"  --timezone <идентификатор> часовой пояс расписания; по умолчанию {TimeZoneInfo.Local.Id}");
    }

    private static int ReadInt(string[] arguments, ref int index, string option)
    {
        if (++index >= arguments.Length
            || !int.TryParse(arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Для {option} нужно указать целое число.");
        }

        return value;
    }

    private static string ReadString(string[] arguments, ref int index, string option)
    {
        if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new InvalidOperationException($"Для {option} нужно указать значение.");
        }

        return arguments[index];
    }
}
