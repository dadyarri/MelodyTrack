#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../MelodyTrack.Backend/MelodyTrack.Backend.csproj
#:package Bogus@35.6.5

using System.Globalization;
using System.Text.Json;
using Bogus;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

const string demoMarker = "Демо · рекомендации";

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

    if (await db.ClientSources.AsNoTracking().AnyAsync(source => source.Name == demoMarker))
    {
        Console.WriteLine("Демонстрационные данные уже есть: найден источник «Демо · рекомендации». Ничего не изменено.");
        return 0;
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

    Randomizer.Seed = new Random(options.Seed);
    var faker = new Faker("ru");
    var random = new Random(options.Seed);
    var periodStart = Utc(options.Year, 1, 1);
    var periodEnd = options.Year == nowUtc.Year
        ? nowUtc.Date
        : Utc(options.Year, 12, 31);
    var scheduleEnd = options.Year == nowUtc.Year
        ? Min(periodEnd.AddDays(21), Utc(options.Year, 12, 31))
        : periodEnd;

    await using var transaction = await db.Database.BeginTransactionAsync();

    var sources = CreateSources();
    var services = CreateServices(periodStart, periodEnd);
    var categories = CreateExpenseCategories();
    var clients = CreateClients(faker, random, sources, options.ClientCount, periodStart, periodEnd);

    await db.ClientSources.AddRangeAsync(sources);
    await db.Services.AddRangeAsync(services.Select(item => item.Service));
    await db.ServicePriceHistory.AddRangeAsync(services.SelectMany(item => item.Prices));
    await db.ExpenseCategories.AddRangeAsync(categories);
    await db.Clients.AddRangeAsync(clients);

    var appointments = CreateAppointments(
        random,
        clients,
        services,
        providers,
        periodStart,
        scheduleEnd,
        nowUtc);
    var payments = CreatePayments(random, appointments, services, nowUtc, periodEnd);
    var expenses = CreateExpenses(random, categories, periodStart, periodEnd);

    await db.Appointments.AddRangeAsync(appointments);
    await db.Payments.AddRangeAsync(payments);
    await db.Expenses.AddRangeAsync(expenses);

    var recurrenceRules = new List<AppointmentRecurrenceRule>();
    if (options.Year == nowUtc.Year)
    {
        var weekly = await db.RecurrenceTypes.SingleAsync(type => type.Type == AppointmentRecurrenceType.Weekly);
        recurrenceRules = CreateRecurrenceRules(random, clients, services, providers, weekly, nowUtc);
        await db.RecurrenceRules.AddRangeAsync(recurrenceRules);
    }

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    var connection = new NpgsqlConnectionStringBuilder(databaseUrl);
    Console.WriteLine($"Готово: локальная база {connection.Database} на {connection.Host} заполнена за период с {periodStart:dd.MM.yyyy}.");
    Console.WriteLine($"Клиенты: {clients.Count}; занятия: {appointments.Count}; оплаты: {payments.Count}; расходы: {expenses.Count}; будущие правила: {recurrenceRules.Count}.");
    Console.WriteLine($"Преподаватели в расписании: {string.Join(", ", providers.Select(user => $"{user.FirstName} {user.LastName}"))}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Не удалось заполнить базу: {exception.Message}");
    return 1;
}

static List<ClientSource> CreateSources()
{
    return
    [
        Entity(new ClientSource { Name = "Демо · рекомендации" }),
        Entity(new ClientSource { Name = "Демо · ВКонтакте" }),
        Entity(new ClientSource { Name = "Демо · Яндекс Карты" }),
        Entity(new ClientSource { Name = "Демо · сайт" }),
        Entity(new ClientSource { Name = "Демо · вывеска" })
    ];
}

static List<SeedService> CreateServices(DateTime periodStart, DateTime periodEnd)
{
    var definitions = new[]
    {
        new ServiceDefinition("Демо · Вокал", "Вокал", "Индивидуальное занятие по вокалу", false, 2_200m),
        new ServiceDefinition("Демо · Фортепиано", "Фортепиано", "Индивидуальное занятие по фортепиано", false, 2_400m),
        new ServiceDefinition("Демо · Гитара", "Гитара", "Индивидуальное занятие по гитаре", false, 2_100m),
        new ServiceDefinition("Демо · Сольфеджио", "Сольфеджио", "Занятие по музыкальной теории", false, 1_800m),
        new ServiceDefinition("Демо · Знакомство", "Пробное занятие", "Первое знакомство с преподавателем", true, 1_000m)
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
        var priceDates = new[]
        {
            periodStart,
            Utc(periodStart.Year, 4, 1),
            Utc(periodStart.Year, 7, 1)
        };
        var prices = priceDates
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

static List<ExpenseCategory> CreateExpenseCategories()
{
    return
    [
        Entity(new ExpenseCategory { Name = "Демо · Аренда" }),
        Entity(new ExpenseCategory { Name = "Демо · Реклама" }),
        Entity(new ExpenseCategory { Name = "Демо · Оборудование" }),
        Entity(new ExpenseCategory { Name = "Демо · Расходники" })
    ];
}

static List<Client> CreateClients(
    Faker faker,
    Random random,
    IReadOnlyList<ClientSource> sources,
    int count,
    DateTime periodStart,
    DateTime periodEnd)
{
    string[] patronymics =
    [
        "Александрович", "Александровна", "Андреевич", "Андреевна", "Игоревич", "Игоревна",
        "Михайлович", "Михайловна", "Павлович", "Павловна", "Сергеевич", "Сергеевна"
    ];
    var earliestCreation = periodStart;
    var latestCreation = Min(periodEnd, periodStart.AddMonths(4));
    var creationRange = Math.Max(1, (latestCreation - earliestCreation).Days);
    var clients = new List<Client>(count);

    for (var index = 0; index < count; index++)
    {
        var firstName = faker.Name.FirstName();
        var lastName = faker.Name.LastName();
        var age = random.Next(8, 62);
        var birthDate = DateOnly.FromDateTime(periodStart.AddYears(-age).AddDays(-random.Next(0, 365)));
        var createdAt = earliestCreation.AddDays(random.Next(creationRange)).AddHours(random.Next(9, 19));
        var contactIndex = index + 1;

        clients.Add(Entity(new Client
        {
            FirstName = firstName,
            LastName = lastName,
            Patronymic = patronymics[random.Next(patronymics.Length)],
            DateOfBirth = birthDate,
            Source = Pick(random, sources),
            CreatedAtUtc = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            IsLeadClosed = random.NextDouble() < 0.12,
            Contacts = Entity(new ClientContacts
            {
                Email = $"demo.client.{contactIndex:000}@example.test",
                Phone = $"+7999{contactIndex % 10_000_000:0000000}",
                Telegram = $"https://t.me/demo_client_{contactIndex:000}",
                Vk = $"https://vk.com/demo_client_{contactIndex:000}"
            })
        }));
    }

    return clients;
}

static List<Appointment> CreateAppointments(
    Random random,
    IReadOnlyList<Client> clients,
    IReadOnlyList<SeedService> services,
    IReadOnlyList<User> providers,
    DateTime periodStart,
    DateTime scheduleEnd,
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

    for (var day = periodStart.Date; day <= scheduleEnd.Date; day = day.AddDays(1))
    {
        if (day.DayOfWeek == DayOfWeek.Sunday)
        {
            continue;
        }

        var eligibleClients = clients
            .Where(client => !client.IsLeadClosed && client.CreatedAtUtc.Date <= day)
            .ToArray();
        if (eligibleClients.Length == 0)
        {
            continue;
        }

        var maximumDailyCount = Math.Min(7, eligibleClients.Length);
        var minimumDailyCount = Math.Min(3, maximumDailyCount);
        var dailyCount = random.Next(minimumDailyCount, maximumDailyCount + 1);
        for (var index = 0; index < dailyCount; index++)
        {
            var providerIndex = index % providers.Count;
            var round = index / providers.Count;
            var startMinute = 9 * 60 + round * 90 + random.Next(0, 3) * 15;
            var start = AtUtc(day, startMinute);
            var service = Pick(random, services);
            var status = start >= nowUtc
                ? AppointmentStatus.Planned
                : PastAppointmentStatus(random);

            appointments.Add(Entity(new Appointment
            {
                Client = Pick(random, eligibleClients),
                Service = service.Service,
                Provider = providers[providerIndex],
                StartDate = start,
                EndDate = start.AddMinutes(service.Service.IsConsultation ? 45 : 60),
                Status = status,
                IsDeleted = start < nowUtc && random.NextDouble() < 0.012,
                LessonNotes = status == AppointmentStatus.Completed && random.NextDouble() < 0.32
                    ? Pick(random, notes)
                    : null
            }));
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
    var payments = new List<Payment>();

    foreach (var appointment in appointments.Where(item =>
                 !item.IsDeleted &&
                 item.StartDate < nowUtc &&
                 item.Status is AppointmentStatus.Completed or AppointmentStatus.Burned))
    {
        if (random.NextDouble() >= 0.78)
        {
            continue;
        }

        var price = PriceAt(priceHistory[appointment.Service.Id], appointment.StartDate);
        var amount = random.NextDouble() < 0.12 ? Math.Round(price * 0.5m, 2) : price;
        var paidAt = appointment.StartDate.AddDays(random.Next(0, 8)).AddHours(random.Next(0, 5));
        paidAt = Min(Min(paidAt, nowUtc), periodEnd.AddDays(1).AddTicks(-1));
        payments.Add(Entity(new Payment
        {
            Client = appointment.Client,
            Service = appointment.Service,
            Amount = amount,
            Date = DateTime.SpecifyKind(paidAt, DateTimeKind.Utc),
            Description = random.NextDouble() < 0.18 ? "Частичная оплата занятия" : "Оплата занятия"
        }));
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

    return payments;
}

static List<Expense> CreateExpenses(
    Random random,
    IReadOnlyList<ExpenseCategory> categories,
    DateTime periodStart,
    DateTime periodEnd)
{
    var descriptions = new Dictionary<string, string[]>
    {
        ["Демо · Аренда"] = ["Аренда студии", "Аренда кабинета"],
        ["Демо · Реклама"] = ["Продвижение объявлений", "Печать листовок", "Реклама в соцсетях"],
        ["Демо · Оборудование"] = ["Стойка для микрофона", "Педаль для инструмента", "Наушники"],
        ["Демо · Расходники"] = ["Ноты и тетради", "Струны", "Канцелярия", "Вода для студии"]
    };
    var expenses = new List<Expense>();

    for (var month = new DateTime(periodStart.Year, periodStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
         month <= periodEnd;
         month = month.AddMonths(1))
    {
        var lastDay = Math.Min(DateTime.DaysInMonth(month.Year, month.Month), periodEnd.Month == month.Month ? periodEnd.Day : 31);
        var monthlyRent = categories[0];
        expenses.Add(Entity(new Expense
        {
            Category = monthlyRent,
            CategoryId = monthlyRent.Id,
            Description = "Аренда студии",
            Amount = random.Next(28, 46) * 1_000m,
            Date = Utc(month.Year, month.Month, Math.Min(5, lastDay), 11)
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
                Date = Utc(month.Year, month.Month, random.Next(1, lastDay + 1), random.Next(9, 20))
            }));
        }
    }

    return expenses;
}

static List<AppointmentRecurrenceRule> CreateRecurrenceRules(
    Random random,
    IReadOnlyList<Client> clients,
    IReadOnlyList<SeedService> services,
    IReadOnlyList<User> providers,
    RecurrenceType weekly,
    DateTime nowUtc)
{
    var activeClients = clients.Where(client => !client.IsLeadClosed).OrderBy(_ => random.Next()).Take(6).ToArray();
    var rules = new List<AppointmentRecurrenceRule>();
    for (var index = 0; index < activeClients.Length; index++)
    {
        var start = nowUtc.Date.AddDays(index + 1).AddHours(10 + index % 4 * 2);
        var weekdayBit = 1 << (((int)start.DayOfWeek + 6) % 7);
        rules.Add(Entity(new AppointmentRecurrenceRule
        {
            Client = activeClients[index],
            Service = Pick(random, services).Service,
            Provider = providers[index % providers.Count],
            StartDate = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndDate = Utc(nowUtc.Year, 12, 31, 23, 59),
            RecurrenceType = weekly,
            RecurrencePattern = weekdayBit
        }));
    }

    return rules;
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

static DateTime AtUtc(DateTime day, int minuteOfDay)
{
    return DateTime.SpecifyKind(day.Date.AddMinutes(minuteOfDay), DateTimeKind.Utc);
}

static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
{
    return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}

static DateTime Min(DateTime left, DateTime right)
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
    return File.Exists(Path.Combine(path, "MelodyTrack.slnx")) &&
           File.Exists(Path.Combine(path, "MelodyTrack.Backend", "Properties", "launchSettings.json"));
}

internal sealed record SeedService(Service Service, List<ServicePrice> Prices);

internal sealed record ServiceDefinition(
    string Name,
    string PublicName,
    string Description,
    bool IsConsultation,
    decimal BasePrice);

internal sealed record SeedOptions(int Year, int ClientCount, int Seed, bool ShowHelp)
{
    public static SeedOptions Parse(string[] arguments, int currentYear)
    {
        var year = currentYear;
        var clientCount = 48;
        int? seed = null;
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

        return new SeedOptions(year, clientCount, seed ?? year * 100 + 1, showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Заполняет локальную базу MelodyTrack русскими демонстрационными данными начиная с января.");
        Console.WriteLine();
        Console.WriteLine("Использование:");
        Console.WriteLine("  dotnet run scripts/SeedLocalData.cs");
        Console.WriteLine("  dotnet run scripts/SeedLocalData.cs -- --year 2026 --clients 60 --seed 42");
        Console.WriteLine();
        Console.WriteLine("Параметры:");
        Console.WriteLine("  --year <год>       год начала данных; по умолчанию текущий");
        Console.WriteLine("  --clients <число>  количество клиентов, от 10 до 500; по умолчанию 48");
        Console.WriteLine("  --seed <число>     seed для повторяемой генерации");
    }

    private static int ReadInt(string[] arguments, ref int index, string option)
    {
        if (++index >= arguments.Length ||
            !int.TryParse(arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"Для {option} нужно указать целое число.");
        }

        return value;
    }
}
