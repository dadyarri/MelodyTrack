using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class TeacherTimezoneRecurrenceTests
{
    [Fact]
    public void GetAppointmentsForRule_WeeklyLocalMondayAtUtcSunday_KeepsLocalWeekday()
    {
        var rule = Rule(AppointmentRecurrenceType.Weekly, new DateTime(2030, 6, 2, 23, 0, 0, DateTimeKind.Utc), "Europe/Moscow", 1);
        var service = new RecurringAppointmentService();

        var appointments = service.GetAppointmentsForRule(rule, rule.StartDate, rule.StartDate.AddDays(14)).ToList();

        appointments.Select(item => item.StartDate.DayOfWeek).ShouldAllBe(day => day == DayOfWeek.Sunday);
        appointments.Count.ShouldBe(3);
        appointments.Select(item => item.RecurrenceOrdinal).ShouldBe(new long?[] { 0, 7, 14 });
        service.GetOccurrence(rule, 7)!.StartDate.ShouldBe(appointments[1].StartDate);
    }

    [Fact]
    public void GetAppointmentsForRule_DaylightSavingTransition_PreservesLocalTimeAndUtcDuration()
    {
        var rule = Rule(AppointmentRecurrenceType.Daily, new DateTime(2030, 3, 30, 11, 0, 0, DateTimeKind.Utc), "Europe/Berlin", 1);
        var service = new RecurringAppointmentService();

        var appointments = service.GetAppointmentsForRule(rule, rule.StartDate, rule.StartDate.AddDays(2)).ToList();

        appointments.Select(item => item.StartDate.Hour).ShouldBe(new[] { 11, 10, 10 });
        appointments.ShouldAllBe(item => item.EndDate - item.StartDate == TimeSpan.FromHours(1));
        appointments.Select(item => TimeZoneInfo.ConvertTimeFromUtc(item.StartDate, TimeZoneInfo.FindSystemTimeZoneById(rule.Timezone)).Hour)
            .ShouldAllBe(hour => hour == 12);
    }

    [Theory]
    [InlineData("Europe/Berlin", 0, 1)]
    [InlineData("Europe/Dublin", 0, 1)]
    public void GetOccurrence_AmbiguousLocalTime_UsesLaterUtcInstant(string timezone, int startHour, int expectedHour)
    {
        var rule = Rule(AppointmentRecurrenceType.Daily, new DateTime(2030, 10, 26, startHour, 30, 0, DateTimeKind.Utc), timezone, 1);
        var service = new RecurringAppointmentService();

        var occurrence = service.GetOccurrence(rule, 1)!;

        occurrence.StartDate.ShouldBe(new DateTime(2030, 10, 27, expectedHour, 30, 0, DateTimeKind.Utc));
        service.GetAppointmentsForRule(rule, occurrence.StartDate.Date, occurrence.StartDate.Date.AddDays(1))
            .ShouldHaveSingleItem().StartDate.ShouldBe(occurrence.StartDate);
    }

    [Fact]
    public void GetAppointmentsForRule_ExactEndTimestamp_ExcludesLaterOccurrenceOnSameDay()
    {
        var rule = Rule(AppointmentRecurrenceType.Daily, new DateTime(2030, 6, 3, 12, 0, 0, DateTimeKind.Utc), "UTC", 1);
        rule.EndDate = rule.StartDate.AddDays(1).AddHours(-1);

        var appointments = new RecurringAppointmentService().GetAppointmentsForRule(rule, rule.StartDate, rule.StartDate.AddDays(2)).ToList();

        appointments.ShouldHaveSingleItem().StartDate.ShouldBe(rule.StartDate);
    }

    [Fact]
    public void GetOccurrence_MonthWithoutSelectedDay_PreservesIdentityOfFollowingMonth()
    {
        var rule = Rule(AppointmentRecurrenceType.Monthly, new DateTime(2030, 1, 31, 12, 0, 0, DateTimeKind.Utc), "UTC", 31);
        var service = new RecurringAppointmentService();

        service.GetOccurrence(rule, 1).ShouldBeNull();
        var march = service.GetOccurrence(rule, 2)!;

        march.StartDate.ShouldBe(new DateTime(2030, 3, 31, 12, 0, 0, DateTimeKind.Utc));
        service.GetOrdinal(rule, march.StartDate).ShouldBe(2);
    }

    private static AppointmentRecurrenceRule Rule(AppointmentRecurrenceType type, DateTime start, string timezone, int pattern) => new()
    {
        Id = Ulid.NewUlid(), Client = new Client { Id = Ulid.NewUlid(), FirstName = "Test", LastName = "Client", CreatedAtUtc = start, Contacts = new ClientContacts() },
        Service = new Service { Id = Ulid.NewUlid(), Name = "Test" },
        RecurrenceType = new RecurrenceType { Id = Ulid.NewUlid(), Type = type, DisplayName = "Test" },
        StartDate = start, Timezone = timezone, RecurrencePattern = pattern
    };
}
