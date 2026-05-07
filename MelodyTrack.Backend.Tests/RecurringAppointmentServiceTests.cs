using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class RecurringAppointmentServiceTests
{
    private readonly IRecurringAppointmentService _service = new RecurringAppointmentService();

    private static AppointmentRecurrenceRule CreateRule(
        AppointmentRecurrenceType type,
        DateTime startDate,
        DateTime? endDate = null,
        int? pattern = null)
    {
        var client = new Client { Id = Ulid.NewUlid(), FirstName = "Test", LastName = "Name", Contacts = new ClientContacts() };
        var service = new Service { Id = Ulid.NewUlid(), Name = "Test Service" };
        var recurrenceType = new RecurrenceType { Id = Ulid.NewUlid(), Type = type, DisplayName = type.ToString() };

        return new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = null,
            StartDate = startDate,
            EndDate = endDate,
            RecurrenceType = recurrenceType,
            RecurrencePattern = pattern
        };
    }

    #region Daily Recurrence Tests

    [Fact]
    public void GenerateDailyAppointments_WithinValidRange_CreatesAppointmentsForEachDay()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 30, 0, DateTimeKind.Utc); // 2:30 PM
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldNotBeEmpty();
        // Should create appointments from today until now + 7 days
        appointments.Count.ShouldBe(7);

        // Check times are preserved
        foreach (var apt in appointments)
        {
            apt.StartDate.Hour.ShouldBe(14);
            apt.StartDate.Minute.ShouldBe(30);
            apt.EndDate.ShouldBe(apt.StartDate.AddHours(1));
        }
    }

    [Fact]
    public void GenerateDailyAppointments_WithEndDate_StopsAtEndDate()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 30, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2025, 11, 16, 23, 59, 59, DateTimeKind.Utc); // 2 days later
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime, endDate);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.Count.ShouldBe(3); // 14th, 15th, 16th
        appointments[0].StartDate.ShouldBeLessThan(appointments[1].StartDate);
    }

    [Fact]
    public void GenerateDailyAppointments_RuleStartsInFuture_SkipsPastDates()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 16, 14, 30, 0, DateTimeKind.Utc); // 2 days in future
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldNotBeEmpty();
        // First appointment should be on the 16th
        appointments[0].StartDate.Day.ShouldBe(16);
    }

    [Fact]
    public void GenerateDailyAppointments_PreservesTimeOfDay()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 9, 45, 30, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        foreach (var apt in appointments)
        {
            apt.StartDate.Hour.ShouldBe(9);
            apt.StartDate.Minute.ShouldBe(45);
            apt.StartDate.Second.ShouldBe(30);
        }
    }

    #endregion

    #region Weekly Recurrence Tests

    [Fact]
    public void GenerateWeeklyAppointments_SingleDay_CreatesAppointmentsOnlyForSelectedDay()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, 1); // Monday only (2^0)

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        // Current week: Monday hasn't passed, next week: Monday will appear
        appointments.ShouldNotBeEmpty();
        foreach (var apt in appointments)
        {
            apt.StartDate.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        }
    }

    [Fact]
    public void GenerateWeeklyAppointments_MultipleDays_CreatesAppointmentsForAllSelectedDays()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var pattern = 3; // Monday (1) + Tuesday (2) = 3
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, pattern);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        var days = appointments.Select(a => a.StartDate.DayOfWeek).Distinct().ToList();
        days.ShouldContain(DayOfWeek.Monday);
        days.ShouldContain(DayOfWeek.Tuesday);
        days.ShouldNotContain(DayOfWeek.Wednesday);
    }

    [Fact]
    public void GenerateWeeklyAppointments_WithWeekendDays_IncludesSaturday()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var pattern = 32; // Saturday (2^5)
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, pattern);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldNotBeEmpty();
        foreach (var apt in appointments)
        {
            apt.StartDate.DayOfWeek.ShouldBe(DayOfWeek.Saturday);
        }
    }

    [Fact]
    public void GenerateWeeklyAppointments_WithSunday_IncludesSundayCorrectly()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var pattern = 64; // Sunday (2^6)
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, pattern);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldNotBeEmpty();
        foreach (var apt in appointments)
        {
            apt.StartDate.DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        }
    }

    [Fact]
    public void GenerateWeeklyAppointments_AllWeekdays_CreatesAppointmentsForMondayToFriday()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var pattern = 31; // Monday (1) + Tuesday (2) + Wednesday (4) + Thursday (8) + Friday (16)
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, pattern);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        var days = appointments.Select(a => a.StartDate.DayOfWeek).Distinct().ToList();
        days.Count.ShouldBe(5);
        days.ShouldContain(DayOfWeek.Monday);
        days.ShouldContain(DayOfWeek.Tuesday);
        days.ShouldContain(DayOfWeek.Wednesday);
        days.ShouldContain(DayOfWeek.Thursday);
        days.ShouldContain(DayOfWeek.Friday);
    }

    [Fact]
    public void GenerateWeeklyAppointments_WithEndDate_StopsAtEndDate()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc); // Friday
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2025, 11, 17, 23, 59, 59, DateTimeKind.Utc); // Monday of next week
        var pattern = 1; // Monday only
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, endDate, pattern);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        // Should only include Monday of current/next week up to end date
        appointments.All(a => a.StartDate <= endDate).ShouldBeTrue();
    }

    [Fact]
    public void GenerateWeeklyAppointments_NullPattern_ReturnsEmpty()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldBeEmpty();
    }

    [Fact]
    public void GenerateWeeklyAppointments_ZeroPattern_ReturnsEmpty()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Weekly, ruleStartTime, null, 0);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldBeEmpty();
    }

    #endregion

    #region Monthly Recurrence Tests

    [Fact]
    public void GenerateMonthlyAppointments_ValidDay_CreatesAppointmentForThatDayOfMonth()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 15); // 15th of each month

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldNotBeEmpty();
        foreach (var apt in appointments)
        {
            apt.StartDate.Day.ShouldBe(15);
        }
    }

    [Fact]
    public void GenerateMonthlyAppointments_CurrentAndNextMonth_CreatesBothAppointments()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 01, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 20); // 20th of each month

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.Count.ShouldBe(1);
        appointments[0].StartDate.Day.ShouldBe(20);
        appointments[0].StartDate.Month.ShouldBe(11); // November
    }

    [Fact]
    public void GenerateMonthlyAppointments_DayAlreadyPassed_SkipsCurrentMonth()
    {
        // Arrange
        var now = new DateTime(2025, 11, 20, 10, 0, 0, DateTimeKind.Utc); // 20th
        var ruleStartTime = new DateTime(2025, 11, 01, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 15); // 15th (already passed)

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.ShouldBeEmpty();
    }

    [Fact]
    public void GenerateMonthlyAppointments_InvalidDay_SkipsMonthWithoutThatDay()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 01, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 31); // 31st

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        // November has 30 days, so should skip November
        appointments.Count.ShouldBe(0);
    }

    [Fact]
    public void GenerateMonthlyAppointments_WithEndDate_StopsAtEndDate()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 01, 14, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2025, 11, 25, 23, 59, 59, DateTimeKind.Utc); // Before Dec 20th
        var rule = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, endDate, 20);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.Count.ShouldBe(1);
        appointments[0].StartDate.Month.ShouldBe(11);
        appointments[0].StartDate.Day.ShouldBe(20);
    }

    [Fact]
    public void GenerateMonthlyAppointments_InvalidPattern_ReturnsEmpty()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 01, 14, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var rule1 = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime);
        _service.GetAppointmentsForRule(rule1, now).ShouldBeEmpty();

        var rule2 = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 0);
        _service.GetAppointmentsForRule(rule2, now).ShouldBeEmpty();

        var rule3 = CreateRule(AppointmentRecurrenceType.Monthly, ruleStartTime, null, 32);
        _service.GetAppointmentsForRule(rule3, now).ShouldBeEmpty();
    }

    #endregion

    #region General Tests

    [Fact]
    public void GetAppointmentsForRule_AllAppointmentsHaveCorrectProperties()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        foreach (var apt in appointments)
        {
            apt.Client.ShouldNotBeNull();
            apt.Service.ShouldNotBeNull();
            apt.IsCompleted.ShouldBeFalse();
            apt.IsCanceled.ShouldBeFalse();
            apt.RecurringRule.ShouldBe(rule);
            apt.StartDate.ShouldBeLessThan(apt.EndDate);
            apt.EndDate.ShouldBe(apt.StartDate.AddHours(1));
        }
    }

    [Fact]
    public void GetAppointmentsForRule_AllAppointmentsWithinRuleTimeRange()
    {
        // Arrange
        var now = new DateTime(2025, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var ruleStartTime = new DateTime(2025, 11, 14, 14, 0, 0, DateTimeKind.Utc);
        var ruleEndDate = new DateTime(2025, 11, 18, 23, 59, 59, DateTimeKind.Utc);
        var rule = CreateRule(AppointmentRecurrenceType.Daily, ruleStartTime, ruleEndDate);

        // Act
        var appointments = _service.GetAppointmentsForRule(rule, now).ToList();

        // Assert
        appointments.All(a => a.StartDate >= rule.StartDate.Date).ShouldBeTrue();
        appointments.All(a => a.StartDate.Date <= ruleEndDate.Date).ShouldBeTrue();
    }

    #endregion

}